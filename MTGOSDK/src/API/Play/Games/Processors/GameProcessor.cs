/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Intermediate;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Proxy;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Manages per-game state: hook queuing/draining, snapshot construction,
/// and dispatching to registered <see cref="IGameStateProcessor"/> components.
/// </summary>
public sealed class GameProcessor : IDisposable
{
  private static readonly object s_scopeLock = new();
  private static readonly ConditionalWeakTable<Game, GameProcessor> s_scopes = new();

  /// <summary>
  /// Gets the processing scope for this Game wrapper. Creating a scope does
  /// not activate snapshot hooks; registration activates them when needed.
  /// </summary>
  public static GameProcessor ForGame(Game game)
  {
    ArgumentNullException.ThrowIfNull(game);
    lock (s_scopeLock)
    {
      if (s_scopes.TryGetValue(game, out var scope) && !scope._disposed)
        return scope;
      s_scopes.Remove(game);
      return s_scopes.GetValue(game, static owner => new GameProcessor(owner));
    }
  }

  internal static void ClearEvents(Game game)
  {
    GameProcessor? processor;
    lock (s_scopeLock)
      s_scopes.TryGetValue(game, out processor);
    processor?.Dispose();

    if (s_activeProcessors.IsEmpty) return;
    int gameId = processor?._activeGameId ?? game.Id;
    if (s_activeProcessors.TryGetValue(gameId, out var active))
      active.Dispose();
  }

  internal static ProcessorEvent<ZoneChangeEventArgs> ZoneChanged(Game game) =>
    new(game, typeof(ZoneChangeProcessor), () => new ZoneChangeProcessor());

  internal static ProcessorEvent<CardChangedEventArgs> CardChanged(Game game) =>
    new(game, typeof(PropertyChangeProcessor), () => new PropertyChangeProcessor());

  internal static ProcessorEvent<PlayerChangedEventArgs> PlayerChanged(Game game) =>
    new(game, typeof(PropertyChangeProcessor), () => new PropertyChangeProcessor());

  internal static ProcessorEvent<ActionFinalizedEventArgs> ActionFinalized(Game game) =>
    new(game, typeof(ActionProcessor), () => new ActionProcessor());

  internal static ProcessorEvent<PromptChangedEventArgs> PromptChanged(Game game) =>
    new(game, typeof(PromptProcessor), () => new PromptProcessor());

  internal static ProcessorEvent<GameStateChangedEventArgs> StateChanged(Game game) =>
    new(game, typeof(PromptProcessor), () => new PromptProcessor());

  internal static ProcessorEvent<LogMessageCorrelatedEventArgs> LogMessage(Game game) =>
    new(game, typeof(LogMessageProcessor), () => new LogMessageProcessor());

  internal static ProcessorEvent<DamageAssignmentEventArgs> DamageAssignment(Game game) =>
    new(game, typeof(DamageAssignmentProcessor), () => new DamageAssignmentProcessor());

  internal static ProcessorEvent<RevealedCardsEventArgs> RevealedCards(Game game) =>
    new(game, typeof(RevealedZoneProcessor), () => new RevealedZoneProcessor());

  private sealed class PendingHook(
    dynamic instance,
    uint timestamp,
    DateTime actionTimestamp,
    RemoteBatchResult capture,
    RemoteCaptureSequence? sequence)
  {
    public RemoteCaptureSequence? Sequence { get; } = sequence;
    public dynamic Instance { get; } = instance;
    public RemoteBatchResult Capture { get; } = capture;
    public uint Timestamp { get; } = timestamp;
    public DateTime ActionTimestamp { get; } = actionTimestamp;
    public DateTime EnqueuedAtUtc { get; } = DateTime.UtcNow;
  }

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
  private readonly List<Action> _eventCleanup = new();
  private GameReferenceResolver? _references;
  private CardActionRegistry? _cardActions;
  private InteractionModeTracker? _interactionState;
  private int? _activeGameId;
  private volatile bool _disposed;
  private bool _resourcesReleased;

  //
  // Centralized event bus
  //

  private readonly Dictionary<Type, List<Delegate>> _subscriptions = new();

  //
  // Snapshot state
  //

  private Dictionary<int, GameCard> _previous = new();
  private GameStateSnapshot? _lastSnapshot;
  private readonly Dictionary<int, int> _previousHashes = new();
  private Dictionary<int, int> _previousIneligibleHashes = new();
  private readonly Dictionary<int, GameCard> _hiddenCards = new();
  private readonly HashSet<int> _subCardIds = new();
  private Dictionary<int, GamePlayer> _previousPlayers = new();

  //
  // Hook queuing
  //

  private readonly SortedDictionary<uint, Queue<PendingHook>> _pendingHooks = new();

  /// <summary>
  /// Whether a background drain task is currently scheduled for this game.
  /// </summary>
  private bool _pendingDrainScheduled;

  /// <summary>
  /// When set, the drain loop bypasses the holdback window so all pending
  /// hooks are processed immediately. Set by <see cref="WaitForPendingDrain"/>.
  /// </summary>
  private bool _forceImmediateDrain;

  /// <summary>
  /// Waiters completed when the queue empties, processing fails, or the scope ends.
  /// </summary>
  private readonly List<TaskCompletionSource<bool>> _drainWaiters = new();

  /// <summary>
  /// Wakes the drain loop when a caller waits for pending processing.
  /// </summary>
  private readonly SemaphoreSlim _drainWakeup = new(0, 1);

  private uint _lastProcessedTimestamp;
  private int _lastProcessedNonce;

  /// <summary>
  /// Whether subscriptions are ready for snapshot delivery.
  /// </summary>
  public bool IsInitialized { get; private set; }

  /// <summary>
  /// The game instance being processed.
  /// </summary>
  public Game Game { get; }

  /// <summary>
  /// Live reference scope shared by this game's snapshots and processors.
  /// </summary>
  public GameReferenceResolver References
  {
    get
    {
      lock (_lock)
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _references ??= GameReferenceResolver.ForGame((object)Unbind(Unbind(Game)));
      }
    }
  }

  /// <summary>
  /// Lazily starts correlated state observation for captured card actions.
  /// </summary>
  public CardActionRegistry CardActions
  {
    get
    {
      lock (_lock)
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cardActions is null)
        {
          bool activate = _activeGameId is null;
          var registry = new CardActionRegistry();
          registry.Initialize(Game);
          registry.Changed += NotifyCardActionsChanged;
          _cardActions = registry;
          if (activate) Ready();
        }
        return _cardActions;
      }
    }
  }

  /// <summary>
  /// Lazily attaches the duel scene's interaction tracker without starting snapshots.
  /// </summary>
  public InteractionModeTracker InteractionState
  {
    get
    {
      lock (_lock)
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_interactionState is null)
        {
          var tracker = new InteractionModeTracker();
          tracker.Initialize(Game);
          _interactionState = tracker;
        }
        else
        {
          _interactionState.Initialize(Game);
        }
        return _interactionState;
      }
    }
  }

  static GameProcessor()
  {
    RemoteClient.Disposed += ClearScopes;
  }

  private GameProcessor(Game game)
  {
    ArgumentNullException.ThrowIfNull(game);
    Game = game;
    _processors = new List<IGameStateProcessor>();
  }

  private void EnsureActive()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    if (_activeGameId is not null) return;
    int gameId = Game.Id;
    Activate(gameId, this);
    _activeGameId = gameId;
  }

  /// <summary>
  /// Allows queued snapshots to drain after event subscriptions are complete.
  /// </summary>
  public void Ready()
  {
    lock (_lock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      IsInitialized = true;
    }
  }

  /// <summary>
  /// Waits for queued snapshots and their prompt correlations within one timeout.
  /// </summary>
  public bool WaitForPendingProcessing(TimeSpan timeout)
  {
    var elapsed = Stopwatch.StartNew();
    bool drainCompleted = WaitForPendingDrain(timeout);
    if (!drainCompleted) return false;
    var prompt = GetProcessor<PromptProcessor>();
    TimeSpan remaining = timeout == Timeout.InfiniteTimeSpan
      ? timeout : timeout - elapsed.Elapsed;
    bool correlated = prompt?.PromptCorrelated.Wait(remaining < TimeSpan.Zero
      && timeout != Timeout.InfiniteTimeSpan ? TimeSpan.Zero : remaining) ?? true;
    return correlated;
  }

  internal void RegisterEventCleanup(Action cleanup)
  {
    lock (_lock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      _eventCleanup.Add(cleanup);
    }
  }

  /// <summary>
  /// Registers a new state processor and initializes it with the game instance.
  /// </summary>
  public void Register(IGameStateProcessor processor)
  {
    ArgumentNullException.ThrowIfNull(processor);
    lock (_lock)
    {
      EnsureActive();

      EnsureDependencies(processor.GetType());

      try
      {
        processor.Initialize(Game);
      }
      catch
      {
        try
        {
          (processor as IDisposable)?.Dispose();
        }
        catch (Exception disposeError)
        {
          Log.Error(disposeError, "Failed to dispose an uninitialized processor.");
        }
        throw;
      }

      _processors.Add(processor);
    }
  }

  private void EnsureDependencies(Type processorType)
  {
    // Combat state must be populated before property comparisons, regardless
    // of whether the card or player event registers the processor first.
    if (processorType == typeof(PropertyChangeProcessor))
      EnsureRegistered(typeof(CombatProcessor), () => new CombatProcessor());
  }

  /// <summary>
  /// Registers a processor of the given type if not already registered.
  /// Used by <see cref="ProcessorEvent{T}"/> for lazy registration.
  /// </summary>
  internal void EnsureRegistered(Type processorType, Func<IGameStateProcessor> factory)
  {
    lock (_lock)
    {
      EnsureActive();
      if (_processors.Any(processor => processor.GetType() == processorType)) return;
      Register(factory());
    }
  }

  /// <summary>
  /// Waits until queued snapshots have been processed and the drain becomes idle.
  /// </summary>
  /// <param name="timeout">Maximum time to wait.</param>
  /// <returns>True on completion; false on timeout, processing failure, or disposal.</returns>
  public bool WaitForPendingDrain(TimeSpan timeout)
  {
    var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_lock)
    {
      if (_disposed) return false;
      if (!_pendingDrainScheduled) return true;
      _drainWaiters.Add(waiter);
      _forceImmediateDrain = true;
    }

    try
    {
      try
      {
        _drainWakeup.Release();
      }
      catch (SemaphoreFullException)
      {
      }
      return waiter.Task.Wait(timeout) && waiter.Task.Result;
    }
    finally
    {
      lock (_lock)
      {
        _drainWaiters.Remove(waiter);
        _forceImmediateDrain = _drainWaiters.Count > 0;
      }
    }
  }

  //
  // Hook management
  //

  /// <summary>
  /// Retrieves a registered processor of a specific type.
  /// </summary>
  public T? GetProcessor<T>() where T : class, IGameStateProcessor
  {
    lock (_lock)
      return _processors.OfType<T>().FirstOrDefault();
  }

  /// <summary>
  /// Subscribes to events of type <typeparamref name="T"/> emitted by any
  /// processor through <see cref="GameContext.Emit{T}"/>. The handler receives
  /// the event args directly with <see cref="GameEventArgs.Context"/> and
  /// <see cref="GameEventArgs.Nonce"/> already stamped.
  /// </summary>
  public void On<T>(Action<T> handler) where T : GameEventArgs
  {
    ArgumentNullException.ThrowIfNull(handler);
    lock (_lock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
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
  internal void Dispatch<T>(T args, GameContext context, int? nonce = null) where T : GameEventArgs
  {
    List<Delegate> snapshot;
    lock (_lock)
    {
      if (_disposed || !_subscriptions.TryGetValue(typeof(T), out var list)) return;
      snapshot = new List<Delegate>(list);
    }

    // Correlation events use the server nonce; combined state events supply their own.
    args.Context = context;
    args.Nonce = nonce ?? context.Current.Nonce;

    foreach (var handler in snapshot)
    {
      try
      {
        ((Action<T>)handler)(args);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Event handler failed for {0}.", typeof(T).Name);
      }
    }
  }

  /// <summary>
  /// Adds a gameplay-status hook to the pending queue.
  /// Equal timestamps preserve arrival order.
  /// Hooks remain queued until <see cref="Ready"/> allows processing.
  /// </summary>
  public void EnqueuePendingHook(
    dynamic instance,
    (uint Game, DateTime Action) ts,
    RemoteBatchResult capture,
    RemoteCaptureSequence? sequence = null)
  {
    lock (_lock)
    {
      if (_disposed) return;
      if (!_pendingHooks.TryGetValue(ts.Game, out var queue))
      {
        queue = new Queue<PendingHook>();
        _pendingHooks[ts.Game] = queue;
      }

      queue.Enqueue(new PendingHook(instance, ts.Game, ts.Action, capture, sequence));

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
        await _drainWakeup.WaitAsync(s_hookReorderWindow);

        // Dequeue under lock, process outside it.
        // Only one drain task runs at a time (_pendingDrainScheduled),
        // so gameplay messages do not build snapshots concurrently.
        int processedThisCycle = 0;
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
            pending.Timestamp,
            pending.ActionTimestamp,
            pending.Capture,
            pending.Sequence,
            processors);

          // Yield between bursts when several captures are queued.
          if (++processedThisCycle % 3 == 0)
            await Task.Delay(50);
        }

        lock (_lock)
        {
          if (_pendingHooks.Count == 0)
          {
            foreach (var waiter in _drainWaiters) waiter.TrySetResult(true);
            _drainWaiters.Clear();
            _forceImmediateDrain = false;
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
        foreach (var waiter in _drainWaiters) waiter.TrySetResult(false);
        _drainWaiters.Clear();
        _forceImmediateDrain = false;
        _pendingDrainScheduled = false;
      }
      Log.Error(ex, "Hook drain loop failed.");
    }
    finally
    {
      bool release;
      lock (_lock)
      {
        release = _disposed;
        if (release) _pendingDrainScheduled = false;
      }
      if (release) ReleaseResources();
    }
  }

  //
  // Internal helpers
  //

  private bool TryDequeueReadyHook(out PendingHook pending)
  {
    pending = null!;
    if (_disposed || _pendingHooks.Count == 0 || !IsInitialized)
      return false;

    var first = _pendingHooks.First();
    var queue = first.Value;
    var next = queue.Peek();

    // Hold back when several timestamps are queued, unless a caller is waiting.
    if (!_forceImmediateDrain && _pendingHooks.Count > 1 &&
        DateTime.UtcNow - next.EnqueuedAtUtc < s_hookReorderWindow)
      return false;

    pending = queue.Dequeue();
    if (queue.Count == 0)
      _pendingHooks.Remove(first.Key);

    return true;
  }

  /// <summary>
  /// Builds a snapshot from captured gameplay values, then dispatches
  /// it to the registered state processors.
  /// Called from the single drain task — no concurrent access to snapshot state.
  /// </summary>
  private void ProcessGameplayStatus(
    dynamic instance,
    uint ts,
    DateTime actionTs,
    RemoteBatchResult capture,
    RemoteCaptureSequence? sequence,
    List<IGameStateProcessor> processors)
  {
    var capturedPrompt = capture.Values["prompt"];
    var identities = ((object?[])RemoteBatchObject.FromValue(capture.Values["players"])!)
      .Cast<RemoteBatchObject>().ToArray();
    var stateElements = ((object?[])RemoteBatchObject.FromValue(capture.Values["stateElements"])!)
      .Cast<RemoteBatchObject>();

    // Hash captured properties locally and materialize only changed card and player rows.
    var current = new Dictionary<int, GameCard>();
    var currentHashes = new Dictionary<int, int>();
    var currentIneligibleHashes = new Dictionary<int, int>();
    var playerProperties = new Dictionary<int, PropertyContainer>();

    var indicesToMaterialize = new List<int>();
    var playerIndicesToMaterialize = new List<int>();
    int revealedCollection = capture.Values["revealedZoneCollectionId"].GetScalar<int>();

    foreach (var row in capture.Rows.Where(row => row.CollectionOperationId != revealedCollection))
    {
      GameCardCapture.CheckRow(row);

      int i = row.SourceIndex;
      int thingId = row.Values["id"].GetScalar<int>();
      if (thingId < 0) continue;
      int currHash = PropertyContainer.GetSnapshotHash(row.Values["properties"]);

      currentHashes[thingId] = currHash;

      if (IsPlayerThing(thingId))
      {
        currentIneligibleHashes[thingId] = currHash;
        playerIndicesToMaterialize.Add(i);
        continue;
      }

      if (_previousIneligibleHashes.TryGetValue(thingId, out int prevIneligibleHash)
          && prevIneligibleHash == currHash
          && !_hiddenCards.ContainsKey(thingId))
      {
        currentIneligibleHashes[thingId] = currHash;
        continue;
      }

      if (_previousHashes.TryGetValue(thingId, out int prevHash)
          && prevHash == currHash
          && _previous.TryGetValue(thingId, out var cachedCard))
      {
        current[thingId] = CopyCardForProcessing(cachedCard);
        continue;
      }

      indicesToMaterialize.Add(i);
    }

    // Select detached rows from the native postfix before updating SDK caches.
    // Decoding performs no IPC and cannot mutate native cards or zones.
    var captured = GameCardCapture.Decode(capture,
      indicesToMaterialize.ToArray(), playerIndicesToMaterialize.ToArray());
    var revealedCards = GameCardCapture.DecodeRevealed(capture)
      .Select(snapshot => References.CreateCard(snapshot.Properties, snapshot.Values))
      .ToDictionary(card => ((GameCardPartial)Unbind(card)).LogicalAddress
        ?? throw new InvalidOperationException("Revealed card capture has no logical address."));

    // Hidden cards can also receive target enrichment. Copy their local state
    // after capture succeeds so previous snapshots retain their own adapters.
    foreach (var (id, card) in _hiddenCards.ToArray())
      _hiddenCards[id] = CopyCardForProcessing(card);

    foreach (var snapshot in captured)
    {
      var props = snapshot.Properties;
      int tid = props[MagicProperty.THINGNUMBER] ?? -1;
      if (IsPlayerThing(tid))
      {
        playerProperties[tid] = props;
        continue;
      }
      if (tid < 0) continue;

      var card = References.CreateCard(props, snapshot.Values);

      // Keep unused faces in the hidden pool for cast correlation. Zone IDs
      // identify instances, so compare resolved names rather than enum ordinals.
      string? typeCode = (string?)props[MagicProperty.DIGITAL_OBJECT_TYPE_CODE_STRING];
      if (typeCode == "SUBC")
      {
        _subCardIds.Add(tid);
        var zoneName = card.Zone?.Name;
        if (zoneName != "Stack" && zoneName != "Battlefield")
        {
          if (currentHashes.TryGetValue(tid, out int ineligibleHash))
            currentIneligibleHashes[tid] = ineligibleHash;
          _hiddenCards[tid] = card;
          continue;
        }
      }
      else
      {
        // A shuffle can reassign a former sub-card's ThingID.
        _subCardIds.Remove(tid);
      }

      // Newly exposed library identities belong in the hidden pool. Preserve
      // normal zone tracking when a known card returns to the library.
      bool isLibraryGhost = card.Zone?.Name == "Library" &&
                            !_hiddenCards.ContainsKey(tid) &&
                            (!_previous.ContainsKey(tid) ||
                             (_previous[tid].Zone?.Name != "Library" &&
                              _previous[tid].CTN != card.CTN));

      if (IsTransientHiddenCard(card) || isLibraryGhost)
      {
        _hiddenCards[card.Id] = card;
        continue;
      }

      _hiddenCards.Remove(card.Id);
      current[tid] = card;
    }

    // Prune stale entries from the hidden pool
    var staleIds = _hiddenCards.Keys.Where(id => !currentHashes.ContainsKey(id)).ToList();
    foreach (var id in staleIds) _hiddenCards.Remove(id);

    // Parse state elements by type.
    // Seed from previous snapshot so ticks without PlayerStatus (e.g. some
    // InteractState updates) can still carry player state forward.
    var playerPartials = new Dictionary<int, GamePlayerPartial>(_previousPlayers.Count);
    foreach (var (idx, prevPlayer) in _previousPlayers)
    {
      if (Unbind(prevPlayer) is GamePlayerPartial prevPartial)
        playerPartials[idx] = GamePlayerPartial.Clone(prevPartial);
    }
    int turnNumber = 0;
    GamePhase currentPhase = GamePhase.Invalid;
    byte promptedPlayer = byte.MaxValue;
    string promptText = string.Empty;
    uint interactionTimestamp = 0;

    var promptIdentity = (RemoteBatchObject?)RemoteBatchObject.FromValue(capturedPrompt);
    bool hasPromptElement = false;
    StateElementType stateType = StateElementType.Invalid;
    var rawDamageAssignments = new List<DamageAssignmentPartial>();
    foreach (var element in stateElements)
    {
      stateType = element.Get<StateElementType>("Type");
      switch (stateType)
      {
        case StateElementType.PlayerStatus:
          Dictionary<int, GamePlayerPartial> parsedPlayers =
            GamePlayerPartial.ParseFromStatusElement(element, identities, References);
          foreach (var entry in parsedPlayers)
          {
            if (playerPartials.TryGetValue(entry.Key, out var prior))
              entry.Value.CopyTransientStateFrom(prior);
          }
          playerPartials = parsedPlayers;
          break;
        case StateElementType.ManaPool:
          GamePlayerPartial.UpdateManaPool(playerPartials, element);
          break;
        case StateElementType.TurnStep:
        case StateElementType.InteractState:
          hasPromptElement = true;
          turnNumber = element.Get<int>("TurnNumber");
          currentPhase = element.Get<GamePhase>("CurrentPhase");
          promptedPlayer = element.Get<byte>("PromptedPlayer");
          promptText = MTGOSDK.API.MtgoTextNormalizer.NormalizeText(
            GamePrompt.RemoveMarkup(element.Get<string?>("PromptText") ?? string.Empty));
          interactionTimestamp = element.Get<uint>("TimeStamp");
          break;
        case StateElementType.DamageAssignment:
        {
          var distributions = new List<DistributionPartial>();
          var thingsToDamage = element.Get<object?[]?>("ThingsToDamage");
          if (thingsToDamage != null)
          {
            foreach (RemoteBatchObject info in thingsToDamage)
            {
              distributions.Add(new DistributionPartial(
                thingId: info.Get<int>("ThingId"),
                amount:  info.Get<int>("Amount"),
                minimum: info.Get<int>("Minimum"),
                maximum: info.Get<int>("Maximum")));
            }
          }
          rawDamageAssignments.Add(new DamageAssignmentPartial(
            damagingThingId: element.Get<int>("DamagingThingId"),
            damageToDeal:    element.Get<int>("DamageToDeal"),
            distributions:   distributions));
          break;
        }
      }
    }

    if (!hasPromptElement && promptIdentity != null)
    {
      turnNumber = promptIdentity.Get<int>("TurnNumber");
      currentPhase = promptIdentity.Get<GamePhase>("CurrentPhase");
      promptedPlayer = promptIdentity.Get<byte>("PromptedPlayer");
      promptText = MtgoTextNormalizer.NormalizeText(GamePrompt.RemoveMarkup(promptIdentity.Get<string>("Text")));
      interactionTimestamp = promptIdentity.Get<uint>("Timestamp");
    }
    GamePlayerPartial.ApplyMessageState(
      playerPartials,
      playerProperties,
      capture.Values["priorityPlayer"].GetScalar<int>());

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
      HiddenCards          = new Dictionary<int, GameCard>(_hiddenCards),
      RevealedCards        = revealedCards,
      Players              = players,
    };

    var previousSnapshot = _lastSnapshot ?? new GameStateSnapshot
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
      HiddenCards          = new Dictionary<int, GameCard>(),
      RevealedCards        = new Dictionary<ulong, GameCard>(),
      Players              = _previousPlayers,
    };

    bool isIntermediate = currentSnapshot.Nonce == _lastProcessedNonce
      && _lastProcessedTimestamp != 0;

    var context = new GameContext
    {
      Current    = currentSnapshot,
      Previous   = previousSnapshot,
      Processor  = this,
      CapturedPrompt = capturedPrompt,
      CaptureSequence = sequence,
      SubCardIds = new HashSet<int>(_subCardIds),
    };

    // Intermediate ticks carry state changes within the same prompt. Damage
    // assignments and interaction events are dispatched only for a new prompt.
    if (!isIntermediate)
    {
      _lastProcessedNonce = currentSnapshot.Nonce;

      // Resolve damage assignment partials against the snapshot's card/player maps.
      foreach (var partial in rawDamageAssignments)
      {
        if (currentSnapshot.Cards.TryGetValue(partial.DamagingThingId, out var sourceCard))
          partial.SourceCard = Unbind(sourceCard);

        foreach (var dist in partial.DistributionPartials)
        {
          if (currentSnapshot.Cards.TryGetValue(dist.ThingId, out var targetCard))
            dist.TargetObject = Unbind(targetCard);
          else if (currentSnapshot.Players.TryGetValue(dist.ThingId, out var targetPlayer))
            dist.TargetObject = Unbind(targetPlayer);
        }

        context.DamageAssignments.Add(new CombatDamageAssignmentAction(partial));
      }
    }

    // Target backfills must precede property comparisons on every tick.
    processors.OfType<ActionProcessor>().FirstOrDefault()?.BackfillPendingTargets(context);
    foreach (var processor in processors)
    {
      if (!isIntermediate || processor is IIntermediateTickProcessor)
      {
        processor.Process(context);
      }
    }

    // Publish one combined update after every processor has applied this message.
    processors.OfType<PromptProcessor>().FirstOrDefault()?.Complete(context);

    // Retain the state used to compare the next message.
    _previous = current;
    _lastSnapshot = currentSnapshot;
    _previousPlayers = players;
    _previousHashes.Clear();
    foreach (var (tid, hash) in currentHashes)
      _previousHashes[tid] = hash;
    _previousIneligibleHashes = currentIneligibleHashes;
    _lastProcessedTimestamp = ts;
  }

  // Cached native values need no recapture, but processor enrichment is per tick.
  private static GameCard CopyCardForProcessing(GameCard card) =>
    new(((GameCardPartial)Unbind(card)).CopyForProcessing());

  /// <summary>
  /// Matches MTGO's <c>Game.IsPlayer(int)</c> logic.
  /// </summary>
  private static bool IsPlayerThing(int thingNumber) =>
    thingNumber < 6;

  /// <summary>
  /// MTGO emits transient hidden cards with no meaningful timestamp.
  /// Keep them for provenance, but do not emit normal enter/leave events.
  /// Stack zone is exempt: triggered/activated abilities are created fresh
  /// (SourceId == -1) but are legitimate visible game objects.
  /// </summary>
  private static bool IsTransientHiddenCard(GameCard card) =>
    card.Zone?.Name != "Stack" &&
    (card.Timestamp <= 0 || card.SourceId == -1);

  //
  // Static routing
  //

  internal static event Action<GameCard, IReadOnlyList<CardAction>>? CardActionsChanged;

  private static void ClearScopes(object? sender, EventArgs args)
  {
    GameProcessor[] scopes;
    lock (s_scopeLock)
      scopes = s_scopes.Select(pair => pair.Value).ToArray();
    foreach (var scope in scopes)
      scope.Dispose();

    s_bufferedHooks.Clear();
    s_notifiedGames.Clear();
  }

  internal static void ObserveCardActions()
  {
    EnsureHookInitialized();
    foreach (var processing in s_activeProcessors.Values)
    {
      lock (processing._lock)
      {
        if (!processing._disposed)
          _ = processing.CardActions;
      }
    }
  }

  private static void NotifyCardActionsChanged(
    (GameCard Card, IReadOnlyList<CardAction> Actions) update)
  {
    var handlers = CardActionsChanged;
    if (handlers is null) return;

    foreach (Action<GameCard, IReadOnlyList<CardAction>> handler in handlers.GetInvocationList())
    {
      try
      {
        handler(update.Card, update.Actions);
      }
      catch (Exception error)
      {
        Log.Error(error, "GameCard.ActionsChanged handler failed.");
      }
    }
  }

  /// <summary>
  /// Maps game IDs to their active <see cref="GameProcessor"/> instances
  /// so the static <c>HandleGamePlayStatus</c> hook can route to the
  /// correct processor.
  /// </summary>
  private static readonly ConcurrentDictionary<int, GameProcessor>
    s_activeProcessors = new();

  /// <summary>
  /// Buffers gameplay-status messages that arrive for a game before its
  /// processor is activated. Replayed by <see cref="Activate"/> so that
  /// the first snapshot is never silently dropped.
  /// </summary>
  private static readonly ConcurrentDictionary<int, ConcurrentQueue<PendingHook>>
    s_bufferedHooks = new();

  /// <summary>
  /// Registers a processor in the static routing table, replays any
  /// buffered hooks, and ensures the <c>HandleGamePlayStatus</c> hook
  /// is installed.
  /// </summary>
  internal static void Activate(int gameId, GameProcessor processor)
  {
    EnsureHookInitialized();
    s_activeProcessors[gameId] = processor;

    // Replay any hooks that arrived before the processor was activated.
    if (s_bufferedHooks.TryRemove(gameId, out var queue))
    {
      while (queue.TryDequeue(out var hook))
      {
        processor.EnqueuePendingHook((object)hook.Instance,
          (hook.Timestamp, hook.ActionTimestamp), hook.Capture, hook.Sequence);
      }
    }
  }

  /// <summary>
  /// Ends this scope, clears its event handlers and routing, and releases owned
  /// components after any snapshot already in progress finishes.
  /// </summary>
  public void Dispose()
  {
    Action[] cleanup;
    bool release;
    lock (_lock)
    {
      if (_disposed) return;
      _references?.StopSharing();
      _disposed = true;
      if (_activeGameId is int gameId
        && ((ICollection<KeyValuePair<int, GameProcessor>>)s_activeProcessors)
          .Remove(new(gameId, this)))
        s_bufferedHooks.TryRemove(gameId, out _);
      cleanup = _eventCleanup.ToArray();
      _eventCleanup.Clear();
      _subscriptions.Clear();
      _pendingHooks.Clear();
      foreach (var waiter in _drainWaiters) waiter.TrySetResult(false);
      _drainWaiters.Clear();
      release = !_pendingDrainScheduled;
    }
    lock (s_scopeLock)
    {
      if (s_scopes.TryGetValue(Game, out var scope) && ReferenceEquals(scope, this))
        s_scopes.Remove(Game);
    }
    foreach (var clear in cleanup) clear();
    if (release) ReleaseResources();
  }

  private void ReleaseResources()
  {
    List<IDisposable> resources;
    lock (_lock)
    {
      if (_resourcesReleased) return;
      _resourcesReleased = true;
      resources = _processors.OfType<IDisposable>().ToList();
      if (_cardActions is not null) resources.Add(_cardActions);
      if (_interactionState is not null) resources.Add(_interactionState);
      if (_references is not null) resources.Add(_references);
      _processors.Clear();
      _previous = new();
      _lastSnapshot = null;
      _previousHashes.Clear();
      _previousIneligibleHashes.Clear();
      _hiddenCards.Clear();
      _subCardIds.Clear();
      _previousPlayers = new();
    }
    foreach (var resource in resources)
    {
      try
      {
        resource.Dispose();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to dispose game processing component.");
      }
    }
  }

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
  public static void EnsureHookInitialized()
  {
    s_handleGamePlayStatus.EnsureInitialize();
  }

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
      new CapturedEventHook((capture, instance, args) =>
      {
        var captured = capture.Capture
          ?? throw new InvalidOperationException("Gameplay hook did not provide its capture.");
        var values = captured.Values;
        int gameId = (int)values["gameId"].GetScalar<uint>();
        var ts = (values["timestamp"].GetScalar<uint>(), capture.Timestamp);
        var prompt = values["prompt"];
        if (prompt.Kind != RemoteBatchValueKind.Null)
        {
          prompt.Members["TurnNumber"] = values["turnNumber"];
          prompt.Members["CurrentPhase"] = values["phase"];
        }

        // Notify subscribers of new games exactly once.
        // The subscriber's event subscriptions call Activate() synchronously,
        // so the routing check below will find the processor.
        if (!s_activeProcessors.ContainsKey(gameId)
            && s_notifiedGames.TryAdd(gameId, 0))
        {
          OnNewGame?.Invoke(new Game(instance));
        }

        // A global card subscription also observes games without per-game subscribers.
        // Reuse an active scope so the same capture is not processed twice.
        if (CardActionsChanged is not null)
        {
          if (!s_activeProcessors.TryGetValue(gameId, out var processing))
            processing = ForGame(new Game(instance));
          lock (processing._lock)
          {
            if (!processing._disposed)
              _ = processing.CardActions;
          }
        }

        if (s_activeProcessors.TryGetValue(gameId, out var processor))
        {
          processor.EnqueuePendingHook(instance, ts, captured, capture.CaptureSequence);
        }
        else
        {
          // Buffer for replay when Activate() is called.
          var queue = s_bufferedHooks.GetOrAdd(gameId, _ => new());
          queue.Enqueue(new PendingHook(instance, ts.Item1, ts.Item2,
            captured, capture.CaptureSequence));
        }

        return (gameId, ts);
      }),
      PromptProcessor.CreateGameplayCapture(), HarmonyPatchPosition.Postfix
    );
}
