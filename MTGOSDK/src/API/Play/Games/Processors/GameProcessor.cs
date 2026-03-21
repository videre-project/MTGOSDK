/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Proxy;
using MTGOSDK.Core.Remoting;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Manages per-game state: hook queuing/draining, snapshot construction,
/// and dispatching to registered <see cref="IGameStateProcessor"/> components.
/// </summary>
public sealed class GameProcessor
{  
  private sealed class PendingHook(
    dynamic instance,
    dynamic message,
    uint timestamp,
    DateTime actionTimestamp)
  {
    public dynamic Instance { get; } = instance;
    public dynamic Message { get; } = message;
    public uint Timestamp { get; } = timestamp;
    public DateTime ActionTimestamp { get; } = actionTimestamp;
    public DateTime EnqueuedAtUtc { get; } = DateTime.UtcNow;
  }

  /// <summary>
  /// Names of MagicProperty values to EXCLUDE from the snapshot hash.
  /// Passed to the remote helper so it can skip noisy UI/engine properties
  /// while hashing all other properties automatically.
  /// </summary>
  private static readonly string s_hashExcludedPropertyNames =
    PropertyContainer.HashExcludedProperties
      .Select(p => p.ToString())
      .Aggregate((a, b) => a + "|" + b);

  /// <summary>
  /// Small holdback window that allows near-simultaneous out-of-order hooks
  /// to accumulate before we apply them in timestamp order.
  /// </summary>
  private static readonly TimeSpan s_hookReorderWindow =
    TimeSpan.FromMilliseconds(25);

  //
  // Instance state
  //

  private readonly List<IGameStateProcessor> _processors;

  private readonly object _lock = new();

  //
  // Centralized event bus
  //

  private readonly Dictionary<Type, List<Delegate>> _subscriptions = new();

  //
  // Snapshot state
  //

  private Dictionary<int, GameCard> _previous = new();
  private Dictionary<int, GameCard> _previousHiddenCards = new();
  private readonly Dictionary<int, int> _previousHashes = new();
  private Dictionary<int, int> _previousIneligibleHashes = new();
  private readonly Dictionary<int, GameCard> _HiddenCards = new();
  private Dictionary<int, GamePlayer> _previousPlayers = new();

  //
  // Hook queuing
  //

  private readonly SortedDictionary<uint, Queue<PendingHook>> _pendingHooks = new();

  /// <summary>
  /// Whether a background drain task is currently scheduled for this game.
  /// </summary>
  private bool _pendingDrainScheduled;

  private uint _lastProcessedTimestamp;

  public bool IsInitialized { get; set; } = false;

  /// <summary>
  /// The game instance being processed.
  /// </summary>
  public Game Game { get; }

  public GameProcessor(Game game)
  {
    Game = game;
    _processors = new List<IGameStateProcessor>();
  }

  public GameProcessor(Game game, params IGameStateProcessor[] processors)
  {
    Game = game;
    _processors = new List<IGameStateProcessor>(processors);
    foreach (var p in _processors) p.Initialize(Game);
  }

  /// <summary>
  /// Registers a new state processor and initializes it with the game instance.
  /// </summary>
  public void Register(IGameStateProcessor processor)
  {
    lock (_lock)
    {
      _processors.Add(processor);
      processor.Initialize(Game);
    }
  }

  /// <summary>
  /// Registers a processor of the given type if not already registered.
  /// Used by <see cref="ProcessorEvent{T}"/> for lazy registration.
  /// </summary>
  internal void EnsureRegistered(Type processorType, Func<IGameStateProcessor> factory)
  {
    lock (_lock)
    {
      if (_registeredProcessorTypes.Contains(processorType)) return;
      _registeredProcessorTypes.Add(processorType);
      var processor = factory();
      _processors.Add(processor);
      processor.Initialize(Game);
    }
  }

  private readonly HashSet<Type> _registeredProcessorTypes = new();

  /// <summary>
  /// Whether any pending hooks remain in the queue.
  /// </summary>
  private bool HasPendingHooks => _pendingHooks.Count > 0;

  //
  // Hook management
  //

  /// <summary>
  /// Retrieves a registered processor of a specific type.
  /// </summary>
  public T? GetProcessor<T>() where T : class, IGameStateProcessor =>
    _processors.OfType<T>().FirstOrDefault();

  /// <summary>
  /// Subscribes to events of type <typeparamref name="T"/> emitted by any
  /// processor through <see cref="GameContext.Emit{T}"/>. The handler receives
  /// the event args directly with <see cref="GameEventArgs.Context"/> and
  /// <see cref="GameEventArgs.Nonce"/> already stamped.
  /// </summary>
  public void On<T>(Action<T> handler) where T : GameEventArgs
  {
    lock (_lock)
    {
      if (!_subscriptions.TryGetValue(typeof(T), out var list))
      {
        list = new List<Delegate>();
        _subscriptions[typeof(T)] = list;
      }
      list.Add(handler);
    }
  }

  /// <summary>
  /// Dispatches event args to all subscribers of type
  /// <typeparamref name="T"/>. Called by <see cref="GameContext.Emit{T}"/>.
  /// </summary>
  internal void Dispatch<T>(T args, GameContext context) where T : GameEventArgs
  {
    List<Delegate> snapshot;
    lock (_lock)
    {
      if (!_subscriptions.TryGetValue(typeof(T), out var list)) return;
      snapshot = new List<Delegate>(list);
    }

    // Stamp context and nonce from the active snapshot.
    args.Context = context;
    args.Nonce = context.Current.Nonce;

    foreach (var handler in snapshot)
    {
      try { ((Action<T>)handler)(args); }
      catch (Exception ex)
      {
        Log.Error(ex, "Event handler failed for {0}.", typeof(T).Name);
      }
    }
  }

  /// <summary>
  /// Adds a gameplay-status hook to the pending queue.
  /// Equal timestamps preserve arrival order.
  /// </summary>
  public void EnqueuePendingHook(
    dynamic instance,
    dynamic message,
    (uint Game, DateTime Action) ts)
  {
    lock (_lock)
    {
      // No-op until at least one processor is registered.
      if (_processors.Count == 0) return;

      if (!_pendingHooks.TryGetValue(ts.Game, out var queue))
      {
        queue = new Queue<PendingHook>();
        _pendingHooks[ts.Game] = queue;
      }

      queue.Enqueue(new PendingHook(instance, message, ts.Game, ts.Action));

      if (!_pendingDrainScheduled)
      {
        _pendingDrainScheduled = true;
        _ = Task.Run(DrainPendingHooksUntilEmpty);
      }
    }
  }

  /// <summary>
  /// Background drain loop that periodically releases aged hooks from the
  /// pending queue in ascending timestamp order.
  /// </summary>
  private async Task DrainPendingHooksUntilEmpty()
  {
    try
    {
      while (true)
      {
        await Task.Delay(s_hookReorderWindow);

        // Dequeue under lock, process outside it.
        // Only one drain task runs at a time (_pendingDrainScheduled),
        // so snapshot state is never accessed concurrently.
        while (true)
        {
          PendingHook pending;
          List<IGameStateProcessor> processors;

          lock (_lock)
          {
            if (!TryDequeueReadyHook(out pending))
              break;
            processors = new List<IGameStateProcessor>(_processors);
          }

          ProcessGameplayStatus(
            pending.Instance,
            pending.Message,
            pending.Timestamp,
            pending.ActionTimestamp,
            processors);
        }

        lock (_lock)
        {
          if (_pendingHooks.Count == 0)
          {
            _pendingDrainScheduled = false;
            return;
          }
        }
      }
    }
    catch (Exception ex)
    {
      lock (_lock)
      {
        _pendingDrainScheduled = false;
      }
      Log.Error(ex, "Hook drain loop failed.");
    }
  }

  //
  // Internal helpers
  //

  private bool TryDequeueReadyHook(out PendingHook pending)
  {
    pending = null!;
    if (_pendingHooks.Count == 0)
      return false;

    var first = _pendingHooks.First();
    var queue = first.Value;
    var next = queue.Peek();
    if (DateTime.UtcNow - next.EnqueuedAtUtc < s_hookReorderWindow)
      return false;

    pending = queue.Dequeue();
    if (queue.Count == 0)
      _pendingHooks.Remove(first.Key);

    return true;
  }

  /// <summary>
  /// Builds a snapshot from a gameplay-status message, then dispatches
  /// it to all registered analyzers.
  /// Called from the single drain task — no concurrent access to snapshot state.
  /// </summary>
  private void ProcessGameplayStatus(
    dynamic instance,
    dynamic message,
    uint ts,
    DateTime actionTs,
    List<IGameStateProcessor> processors)
  {
    // Compute remote hashes (batches one IPC call instead of N)
    // Returns flat [thingId₀, hash₀, thingId₁, hash₁, …]
    int[] rawHashes = (int[])RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CollectionHelpers",
      "ComputeThingSnapshotHashes",
      null,
      (object)message.ThingElements,
      s_hashExcludedPropertyNames);

    // Build current snapshot
    var current = new Dictionary<int, GameCard>();
    var currentHashes = new Dictionary<int, int>();
    var currentIneligibleHashes = new Dictionary<int, int>();
    int hashHits = 0;
    int ineligibleSkips = 0;

    var indicesToMaterialize = new List<int>();
    {
      int dataPairs = rawHashes.Length / 2;

      for (int i = 0; i < dataPairs; i++)
      {
        int thingId  = rawHashes[i * 2];
        int currHash = rawHashes[i * 2 + 1];

        if (thingId < 0) continue;

        currentHashes[thingId] = currHash;

        if (IsPlayerThing(thingId))
        {
          currentIneligibleHashes[thingId] = currHash;
          ineligibleSkips++;
          continue;
        }

        if (_previousIneligibleHashes.TryGetValue(thingId, out int prevIneligibleHash)
            && prevIneligibleHash == currHash)
        {
          currentIneligibleHashes[thingId] = currHash;
          ineligibleSkips++;
          continue;
        }

        if (_previousHashes.TryGetValue(thingId, out int prevHash)
            && prevHash == currHash
            && _previous.TryGetValue(thingId, out var cachedCard))
        {
          current[thingId] = cachedCard;
          hashHits++;
          continue;
        }

        indicesToMaterialize.Add(i);
      }
    }

    if (indicesToMaterialize.Count > 0)
    {
      dynamic thingElements = message.ThingElements;
      foreach (int idx in indicesToMaterialize)
      {
        dynamic thing = thingElements[idx];
        PropertyContainer props = new(thing.Properties);
        int tid = props[MagicProperty.THINGNUMBER] ?? -1;
        if (tid < 0 || IsPlayerThing(tid))
        {
          if (tid >= 0 && currentHashes.TryGetValue(tid, out int ineligibleHash))
            currentIneligibleHashes[tid] = ineligibleHash;
          continue;
        }

        // Filter out sub-cards (Adventures, Split faces, etc.) unless they're in play
        string? typeCode = (string?)props[MagicProperty.DIGITAL_OBJECT_TYPE_CODE_STRING];
        if (typeCode == "SUBC")
        {
          var zone = (CardZone)(props[MagicProperty.ZONE] ?? (int)CardZone.Invalid);
          if (zone is not CardZone.Battlefield and not CardZone.Stack)
          {
            if (currentHashes.TryGetValue(tid, out int ineligibleHash))
              currentIneligibleHashes[tid] = ineligibleHash;
            continue;
          }
        }

        var card = GameCard.FromProperties(props, instance, thing.FromZone);

        // Identify "Library ghosts": revealed cards with no previous state.
        bool isLibraryGhost = card.Zone?.Name == "Library" &&
                              !_previous.ContainsKey(tid) &&
                              !_HiddenCards.ContainsKey(tid);

        if (IsTransientHiddenCard(card) || isLibraryGhost)
        {
          if (currentHashes.TryGetValue(card.Id, out int hiddenHash))
            currentIneligibleHashes[card.Id] = hiddenHash;
          _HiddenCards[card.Id] = card;
          continue;
        }

        _HiddenCards.Remove(card.Id);
        current[tid] = card;
      }
    }

    // Prune stale entries from the hidden pool
    var staleIds = _HiddenCards.Keys.Where(id => !currentHashes.ContainsKey(id)).ToList();
    foreach (var id in staleIds) _HiddenCards.Remove(id);

    // Parse state elements by type
    var playerPartials = new Dictionary<int, GamePlayerPartial>();
    int turnNumber = 0;
    GamePhase currentPhase = GamePhase.Invalid;
    byte promptedPlayer = byte.MaxValue;
    string promptText = string.Empty;
    uint interactionTimestamp = 0;

    StateElementType stateType = StateElementType.Invalid;
    foreach (var element in message.OtherStateElements)
    {
      stateType = Cast<StateElementType>(element.Type);
      switch (stateType)
      {
        case StateElementType.PlayerStatus:
          playerPartials = GamePlayerPartial.ParseFromStatusElement(element, instance);
          break;
        case StateElementType.ManaPool:
          GamePlayerPartial.UpdateManaPool(playerPartials, element);
          break;
        case StateElementType.TurnStep:
        case StateElementType.InteractState:
          turnNumber = element.TurnNumber ?? 0;
          currentPhase = Cast<GamePhase>(element.CurrentPhase ?? 0);
          promptedPlayer = element.PromptedPlayer ?? byte.MaxValue;
          promptText = element.PromptText ?? string.Empty;
          interactionTimestamp = element.TimeStamp ?? 0;
          break;
      }
    }

    // Convert GamePlayerPartial to GamePlayer wrappers
    var players = new Dictionary<int, GamePlayer>(playerPartials.Count);
    foreach (var (idx, partial) in playerPartials)
    {
      players[idx] = new GamePlayer(partial);
    }

    var clientTs = (DateTime)instance.__timestamp;

    var currentSnapshot = new GameStateSnapshot
    {
      Timestamp            = ts,
      ActionTimestamp      = actionTs,
      ClientTimestamp      = clientTs,
      InteractionTimestamp = interactionTimestamp,
      TurnNumber           = turnNumber,
      CurrentPhase         = currentPhase,
      StateType            = stateType,
      PromptedPlayer       = promptedPlayer,
      PromptText           = promptText,
      Cards                = current,
      HiddenCards          = new Dictionary<int, GameCard>(_HiddenCards),
      Players              = players,
    };

    var previousSnapshot = new GameStateSnapshot
    {
      Timestamp            = _lastProcessedTimestamp,
      ActionTimestamp      = actionTs,
      ClientTimestamp      = clientTs,
      InteractionTimestamp = interactionTimestamp,
      TurnNumber           = turnNumber,
      CurrentPhase         = currentPhase,
      StateType            = stateType,
      PromptedPlayer       = promptedPlayer,
      PromptText           = promptText,
      Cards                = _previous,
      HiddenCards          = _previousHiddenCards,
      Players              = _previousPlayers,
    };

    var context = new GameContext
    {
      Current   = currentSnapshot,
      Previous  = previousSnapshot,
      Processor = this,
    };

    foreach (var processor in processors)
      processor.Process(context);

    // Save state for next diff
    _previous = current;
    _previousHiddenCards = new Dictionary<int, GameCard>(_HiddenCards);
    _previousPlayers = players;
    _previousHashes.Clear();
    foreach (var (tid, hash) in currentHashes)
      _previousHashes[tid] = hash;
    _previousIneligibleHashes = currentIneligibleHashes;
    _lastProcessedTimestamp = ts;
  }

  /// <summary>
  /// Matches MTGO's <c>Game.IsPlayer(int)</c> logic.
  /// </summary>
  private static bool IsPlayerThing(int thingNumber) =>
    thingNumber < 6;

  /// <summary>
  /// MTGO emits transient hidden cards with no meaningful timestamp.
  /// Keep them for provenance, but do not emit normal enter/leave events.
  /// </summary>
  private static bool IsTransientHiddenCard(GameCard card) =>
    card.Timestamp <= 0 ||
    card.SourceId == -1;

  //
  // Static routing
  //

  /// <summary>
  /// Maps game IDs to their active <see cref="GameProcessor"/> instances
  /// so the static <c>HandleGamePlayStatus</c> hook can route to the
  /// correct processor.
  /// </summary>
  private static readonly ConcurrentDictionary<int, GameProcessor>
    s_activeProcessors = new();

  /// <summary>
  /// Registers a processor in the static routing table and ensures the
  /// <c>HandleGamePlayStatus</c> hook is installed.
  /// </summary>
  internal static void Activate(int gameId, GameProcessor processor)
  {
    s_activeProcessors[gameId] = processor;
    s_handleGamePlayStatus.EnsureInitialize();
  }

  /// <summary>
  /// Removes a processor from the static routing table.
  /// </summary>
  internal static void Deactivate(int gameId) =>
    s_activeProcessors.TryRemove(gameId, out _);

  /// <summary>
  /// Fires when a <c>HandleGamePlayStatus</c> message arrives for a game
  /// that has no active processor. Subscribers typically create a
  /// <see cref="Game"/> instance and subscribe to its processor events,
  /// which activates processing for that game automatically.
  /// </summary>
  public static event Action<Game>? OnNewGame;

  /// <summary>
  /// Ensures the static <c>HandleGamePlayStatus</c> hook is installed
  /// so that game detection and routing are active.
  /// </summary>
  public static void EnsureHookInitialized() =>
    s_handleGamePlayStatus.EnsureInitialize();

  /// <summary>
  /// Tracks game IDs for which <see cref="OnNewGame"/> has already fired,
  /// ensuring at-most-once delivery per game.
  /// </summary>
  private static readonly ConcurrentDictionary<int, byte>
    s_notifiedGames = new();

  /// <summary>
  /// Static hook on <c>HandleGamePlayStatus</c> that detects new games
  /// and routes incoming gameplay-status messages to the correct per-game
  /// processor.
  /// </summary>
  private static readonly EventHookProxy<int, (uint, DateTime)>
    s_handleGamePlayStatus = new(
      "WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game",
      "HandleGamePlayStatus",
      new((instance, args) =>
      {
        dynamic message = args[0];
        int gameId = (int)(uint)message.GameID;
        var ts = ((uint)message.GameStateTimestamp, (DateTime)instance.__timestamp);

        // Notify subscribers of new games exactly once.
        // The subscriber's event subscriptions call Activate() synchronously,
        // so the routing check below will find the processor.
        if (!s_activeProcessors.ContainsKey(gameId)
            && s_notifiedGames.TryAdd(gameId, 0))
        {
          OnNewGame?.Invoke(new Game(instance));
        }

        if (s_activeProcessors.TryGetValue(gameId, out var processor))
        {
          processor.EnqueuePendingHook(instance, message, ts);
        }

        return (gameId, ts);
      })
    );
}
