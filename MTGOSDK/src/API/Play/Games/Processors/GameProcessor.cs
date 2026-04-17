/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Collection;
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
  // Revealed-zone tracking
  //
  // MTGO sends revealed cards via a separate HandleRevealedCard message that
  // bypasses the ThingElements snapshot pipeline. We hook it and buffer the
  // (playerNum, CTN) pairs, then merge them into _activeReveals on each
  // ProcessGameplayStatus tick. Departure is detected by checking whether
  // any active card's CTN is still present in the Revealed zone via
  // ThingElements (same-ThingId zone changes appear in context.Current.Cards).
  //

  internal sealed record RevealedCardItem(int PlayerNum, int CTN, string Name);

  private readonly ConcurrentQueue<RevealedCardItem> _pendingReveals = new();
  private List<RevealedCardItem> _activeReveals = new();

  /// <summary>
  /// Consecutive ticks with no new reveals since the last populated batch.
  /// When this reaches <see cref="RevealDepartureThreshold"/> the active
  /// reveal list is considered stale and is cleared.
  /// </summary>
  private int _noRevealTicks = 0;

  /// <summary>
  /// Number of consecutive ticks with no new <c>HandleRevealedCard</c>
  /// callbacks before <see cref="_activeReveals"/> is evicted.
  /// MTGO typically sends zero or one intermediate <c>HandleGamePlayStatus</c>
  /// during an active reveal, so a threshold of 3 provides enough slack for
  /// the asynchronous callback ordering without causing noticeable display lag.
  /// </summary>
  private const int RevealDepartureThreshold = 3;

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
  private readonly HashSet<int> _subCardIds = new();
  private readonly Dictionary<int, dynamic?> _definitionCache = new();
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
  /// Barriers inserted by <see cref="WaitForPendingDrain"/>. Signaled after
  /// the drain loop finishes processing all currently-pending hooks.
  /// </summary>
  private readonly List<ManualResetEventSlim> _drainBarriers = new();

  /// <summary>
  /// Wakes the drain loop from its holdback sleep so it processes barriers
  /// without waiting the full reorder window.
  /// </summary>
  private readonly SemaphoreSlim _drainWakeup = new(0, 1);

  private uint _lastProcessedTimestamp;
  private int _lastProcessedNonce;

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

  /// <summary>
  /// Blocks the calling thread until all currently-pending hooks have been
  /// processed by the drain loop. If no drain loop is active (no pending
  /// hooks), returns immediately.
  /// </summary>
  /// <param name="timeout">Maximum time to wait.</param>
  /// <returns><c>true</c> if the drain completed within the timeout.</returns>
  public bool WaitForPendingDrain(TimeSpan timeout)
  {
    using var barrier = new ManualResetEventSlim(false);
    lock (_lock)
    {
      if (!_pendingDrainScheduled)
        return true; // Nothing pending — drain loop is idle

      _drainBarriers.Add(barrier);
      _forceImmediateDrain = true;
    }
    // Wake the drain loop if it's sleeping in the holdback delay
    try { _drainWakeup.Release(); } catch (SemaphoreFullException) { }
    return barrier.Wait(timeout);
  }

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
  /// Hooks are buffered even before processors register; the drain loop
  /// waits for at least one <see cref="IGameStateProcessor"/> so that the
  /// first snapshot is never silently dropped.
  /// </summary>
  public void EnqueuePendingHook(
    dynamic instance,
    dynamic message,
    (uint Game, DateTime Action) ts)
  {
    lock (_lock)
    {
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
        // Wait for the holdback window OR a forced wakeup from
        // WaitForPendingDrain (whichever comes first).
        var wakeup = _drainWakeup.WaitAsync(s_hookReorderWindow);
        await wakeup;

        // Dequeue under lock, process outside it.
        // Only one drain task runs at a time (_pendingDrainScheduled),
        // so snapshot state is never accessed concurrently.
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
            pending.Message,
            pending.Timestamp,
            pending.ActionTimestamp,
            processors);

          // Yield between snapshots to prevent IPC request floods from
          // monopolizing the Diver.  Each ProcessGameplayStatus call
          // generates multiple IPC requests; processing them back-to-back
          // (especially the initial burst after ReadyProcessor) starves
          // the host process's UI thread of CPU time.
          if (++processedThisCycle % 3 == 0)
            await Task.Delay(50);
        }

        // Signal any drain barriers — all currently-pending hooks have
        // been processed and their events have fired.
        lock (_lock)
        {
          if (_drainBarriers.Count > 0)
          {
            foreach (var barrier in _drainBarriers) barrier.Set();
            _drainBarriers.Clear();
            _forceImmediateDrain = false;
          }

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
        // Signal waiting barriers so they don't hang
        foreach (var barrier in _drainBarriers) barrier.Set();
        _drainBarriers.Clear();
        _forceImmediateDrain = false;
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
    if (_pendingHooks.Count == 0 || !IsInitialized)
      return false;

    var first = _pendingHooks.First();
    var queue = first.Value;
    var next = queue.Peek();

    // Adaptive holdback: if only one timestamp bucket exists the hooks
    // arrived in order and no reordering is needed — process immediately.
    // When multiple buckets exist, apply the full holdback window so
    // out-of-order hooks have time to accumulate before we drain.
    if (!_forceImmediateDrain && _pendingHooks.Count > 1 &&
        DateTime.UtcNow - next.EnqueuedAtUtc < s_hookReorderWindow)
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
            && prevIneligibleHash == currHash
            && !_HiddenCards.ContainsKey(thingId))
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

        var card = GameCard.FromProperties(props, instance, thing.FromZone);

        // Filter out sub-cards (Adventures, Split faces, etc.) unless they're in play.
        // Store them in HiddenCards so ZoneChangeTracker can correlate when cast.
        // Use the resolved zone name rather than the raw zone ID (which is an MTGO
        // instance ID and does not correspond to CardZone enum ordinals).
        // Track SUBC status per ThingID. After a library shuffle, MTGO can
        // reassign a ThingID that was previously SUBC to a regular card, so
        // we must also remove stale entries.
        string? typeCode = (string?)props[MagicProperty.DIGITAL_OBJECT_TYPE_CODE_STRING];
        if (typeCode == "SUBC")
        {
          _subCardIds.Add(tid);
          var zoneName = card.Zone?.Name;
          if (zoneName != "Stack" && zoneName != "Battlefield")
          {
            if (currentHashes.TryGetValue(tid, out int ineligibleHash))
              currentIneligibleHashes[tid] = ineligibleHash;
            _HiddenCards[tid] = card;
            continue;
          }
        }
        else
        {
          // ThingID was previously SUBC but has been reassigned after a
          // shuffle — remove stale entry so it's no longer filtered.
          _subCardIds.Remove(tid);
        }

        // Identify "Library ghosts": revealed cards that either have no
        // previous state, or whose ThingID was reassigned to a different
        // physical card after a shuffle (detected by CTN change). Cards
        // that moved to Library from another zone with the same CTN (e.g.
        // tuck effects) are NOT ghosts — they need normal zone tracking.
        bool isLibraryGhost = card.Zone?.Name == "Library" &&
                              !_HiddenCards.ContainsKey(tid) &&
                              (!_previous.ContainsKey(tid) ||
                               (_previous[tid].Zone?.Name != "Library" &&
                                _previous[tid].CTN != card.CTN));

        if (IsTransientHiddenCard(card) || isLibraryGhost)
        {
          _HiddenCards[card.Id] = card;
          continue;
        }

        _HiddenCards.Remove(card.Id);
        current[tid] = card;
      }
    }

    // Resolve card definitions for newly materialized (or rematerialized) cards.
    // CardDefinition is stable per-ThingID regardless of zone, so carry it
    // forward from the previous partial when available.
    foreach (var (tid, card) in current)
    {
      if (Unbind(card) is not GameCardPartial partial) continue;
      if (partial.CardDefinition != null) continue; // hash-hit reuse — already set

      // Rematerialized card: inherit definition from previous partial
      if (_previous.TryGetValue(tid, out var prevCard)
          && Unbind(prevCard) is GameCardPartial prevPartial)
      {
        if (prevPartial.CardDefinition != null)
        {
          partial.CardDefinition = prevPartial.CardDefinition;
          continue;
        }
      }

      // Newly seen card: resolve via CTN
      int ctn = partial.Properties[MagicProperty.CARDTEXTURE_NUMBER] is int c ? c : 0;
      if (ctn <= 0) continue;

      if (!_definitionCache.TryGetValue(ctn, out var def))
      {
        try { def = Unbind(CollectionManager.GetCardByTextureId(ctn)); }
        catch { def = null; }
        _definitionCache[ctn] = def;
      }

      if (def != null)
        partial.CardDefinition = def;
    }

    // Prune stale entries from the hidden pool
    var staleIds = _HiddenCards.Keys.Where(id => !currentHashes.ContainsKey(id)).ToList();
    foreach (var id in staleIds) _HiddenCards.Remove(id);

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

    StateElementType stateType = StateElementType.Invalid;
    var rawDamageAssignments = new List<DamageAssignmentPartial>();
    foreach (var element in message.OtherStateElements)
    {
      stateType = Cast<StateElementType>(element.Type);
      switch (stateType)
      {
        case StateElementType.PlayerStatus:
          Dictionary<int, GamePlayerPartial> parsedPlayers =
            GamePlayerPartial.ParseFromStatusElement((object) element, (object) instance);
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
          turnNumber = element.TurnNumber ?? 0;
          currentPhase = Cast<GamePhase>(element.CurrentPhase ?? 0);
          promptedPlayer = element.PromptedPlayer ?? byte.MaxValue;
          promptText = MTGOSDK.API.MtgoTextNormalizer.NormalizeText(
            GamePrompt.RemoveMarkup(element.PromptText ?? string.Empty));
          interactionTimestamp = element.TimeStamp ?? 0;
          break;
        case StateElementType.DamageAssignment:
        {
          var distributions = new List<DistributionPartial>();
          dynamic thingsToDamage = element.ThingsToDamage;
          if (thingsToDamage != null)
          {
            foreach (dynamic info in thingsToDamage)
            {
              distributions.Add(new DistributionPartial(
                thingId: (int)info.ThingId,
                amount:  (int)info.Amount,
                minimum: (int)info.Minimum,
                maximum: (int)info.Maximum));
            }
          }
          rawDamageAssignments.Add(new DamageAssignmentPartial(
            damagingThingId: (int)element.DamagingThingId,
            damageToDeal:    (int)element.DamageToDeal,
            distributions:   distributions));
          break;
        }
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

    // Skip duplicate snapshots — MTGO sometimes resends the same game state.
    // The nonce is deterministic from (InteractionTimestamp, PromptedPlayer,
    // PromptText), so identical nonces mean identical interaction context.
    // Hash computation and card materialization are already cheap for
    // duplicates (all cache hits), but this avoids processor dispatch and
    // downstream event handler work entirely.
    int currentNonce = currentSnapshot.Nonce;
    // Drain newly-revealed cards from the hook buffer and build the active list.
    var revealedZoneCards = BuildRevealedZoneCards(instance);

    if (currentNonce == _lastProcessedNonce && _lastProcessedTimestamp != 0)
    {
      // Card/player properties can still change within the same interaction context
      // (e.g., IsAttacking going True during DeclareAttackers before the prompt
      // advances). Run state-tracking processors to capture these intermediate
      // transitions before updating the diff baseline.
      var intermediateContext = new GameContext
      {
        Current          = currentSnapshot,
        Previous         = previousSnapshot,
        Processor        = this,
        SubCardIds        = _subCardIds,
        RevealedZoneCards = revealedZoneCards,
      };

      // Backfill action targets before PropertyChangeTracker runs.
      foreach (var processor in processors)
      {
        if (processor is ActionProcessor ap)
        {
          ap.BackfillPendingTargets(intermediateContext);
          break;
        }
      }

      foreach (var processor in processors)
      {
        if (processor is IIntermediateTickProcessor)
          processor.Process(intermediateContext);
      }

      // Still update diff baseline so the next real state change diffs
      // correctly against the latest data.
      _previous = current;
      _previousHiddenCards = new Dictionary<int, GameCard>(_HiddenCards);
      _previousPlayers = players;
      _previousHashes.Clear();
      foreach (var (tid, hash) in currentHashes)
        _previousHashes[tid] = hash;
      _previousIneligibleHashes = currentIneligibleHashes;
      _lastProcessedTimestamp = ts;
      return;
    }
    _lastProcessedNonce = currentNonce;

    // Resolve damage assignment partials against the snapshot's card/player maps.
    var damageAssignments = new List<CombatDamageAssignmentAction>();
    foreach (var partial in rawDamageAssignments)
    {
      // Resolve source card
      if (currentSnapshot.Cards.TryGetValue(partial.DamagingThingId, out var sourceCard))
        partial.SourceCard = Unbind(sourceCard);

      // Resolve each distribution target
      foreach (var dist in partial.DistributionPartials)
      {
        if (currentSnapshot.Cards.TryGetValue(dist.ThingId, out var targetCard))
          dist.TargetObject = Unbind(targetCard);
        else if (currentSnapshot.Players.TryGetValue(dist.ThingId, out var targetPlayer))
          dist.TargetObject = Unbind(targetPlayer);
      }

      damageAssignments.Add(new CombatDamageAssignmentAction(partial));
    }

    var context = new GameContext
    {
      Current           = currentSnapshot,
      Previous          = previousSnapshot,
      Processor         = this,
      SubCardIds        = _subCardIds,
      DamageAssignments = damageAssignments,
      RevealedZoneCards = revealedZoneCards,
    };

    // Pre-pass: backfill action target IDs into card PropertyContainers
    // before PropertyChangeTracker runs, so card change diffs see all targets.
    foreach (var processor in processors)
    {
      if (processor is ActionProcessor actionProcessor)
      {
        actionProcessor.BackfillPendingTargets(context);
        break;
      }
    }

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
  /// Stack zone is exempt: triggered/activated abilities are created fresh
  /// (SourceId == -1) but are legitimate visible game objects.
  /// </summary>
  private static bool IsTransientHiddenCard(GameCard card) =>
    card.Zone?.Name != "Stack" &&
    (card.Timestamp <= 0 || card.SourceId == -1);

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
  /// Buffers gameplay-status messages that arrive for a game before its
  /// processor is activated. Replayed by <see cref="Activate"/> so that
  /// the first snapshot is never silently dropped.
  /// </summary>
  private static readonly ConcurrentDictionary<int, ConcurrentQueue<(object Instance, object Message, (uint Game, DateTime Action) Ts)>>
    s_bufferedHooks = new();

  /// <summary>
  /// Registers a processor in the static routing table, replays any
  /// buffered hooks, and ensures the <c>HandleGamePlayStatus</c> hook
  /// is installed.
  /// </summary>
  internal static void Activate(int gameId, GameProcessor processor)
  {
    s_activeProcessors[gameId] = processor;
    s_handleGamePlayStatus.EnsureInitialize();
    s_handleRevealedCard.EnsureInitialize();

    // Replay any hooks that arrived before the processor was activated.
    if (s_bufferedHooks.TryRemove(gameId, out var queue))
    {
      while (queue.TryDequeue(out var hook))
      {
        processor.EnqueuePendingHook(hook.Instance, hook.Message, hook.Ts);
      }
    }
  }

  /// <summary>
  /// Removes a processor from the static routing table.
  /// </summary>
  internal static void Deactivate(int gameId)
  {
    s_activeProcessors.TryRemove(gameId, out _);
    s_bufferedHooks.TryRemove(gameId, out _);
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
    s_handleRevealedCard.EnsureInitialize();
  }

  /// <summary>
  /// Tracks game IDs for which <see cref="OnNewGame"/> has already fired,
  /// ensuring at-most-once delivery per game.
  /// </summary>
  private static readonly ConcurrentDictionary<int, byte>
    s_notifiedGames = new();

  /// <summary>
  /// Drains <see cref="_pendingReveals"/> into <see cref="_activeReveals"/>
  /// and builds a synthetic <see cref="GameCard"/> list for each active reveal.
  /// <para>
  /// Departure is detected by a tick-count gate: revealed cards stay active
  /// as long as new <c>HandleRevealedCard</c> callbacks keep arriving. Once
  /// <see cref="RevealDepartureThreshold"/> consecutive ticks pass with no new
  /// arrivals the list is cleared. This avoids querying ThingElements, which
  /// never contains Revealed-zone entries (MTGO sends them via the separate
  /// <c>GAME_PLAYER_REVEALS_CARD</c> message, not via the game-state snapshot).
  /// </para>
  /// </summary>
  internal List<GameCard> BuildRevealedZoneCards(dynamic instance)
  {
    bool anyNewArrivals = false;
    while (_pendingReveals.TryDequeue(out var item))
    {
      anyNewArrivals = true;
      if (!_activeReveals.Any(r => r.PlayerNum == item.PlayerNum && r.CTN == item.CTN))
        _activeReveals.Add(item);
    }

    if (_activeReveals.Count == 0)
    {
      _noRevealTicks = 0;
      return new List<GameCard>();
    }

    if (anyNewArrivals)
    {
      _noRevealTicks = 0;
    }
    else if (++_noRevealTicks >= RevealDepartureThreshold)
    {
      Log.Debug("[GameProcessor] Reveal zone cleared after {0} quiet ticks.", RevealDepartureThreshold);
      _activeReveals.Clear();
      _noRevealTicks = 0;
      return new List<GameCard>();
    }

    var result = new List<GameCard>(_activeReveals.Count);
    foreach (var item in _activeReveals)
    {
      int syntheticId = -(item.PlayerNum * 100000 + item.CTN);
      var props = new PropertyContainer(new Dictionary<MagicProperty, dynamic>
      {
        [MagicProperty.THINGNUMBER]           = syntheticId,
        [MagicProperty.CARDTEXTURE_NUMBER]    = item.CTN,
        [MagicProperty.ZONE]                  = (int)16777215u,
        [MagicProperty.OWNER]                 = item.PlayerNum,
        [MagicProperty.CONTROLLER]            = item.PlayerNum,
        [MagicProperty.CARDNAME_STRING]       = item.Name,
        [MagicProperty.SRC_THING_ID]          = -1,
        [MagicProperty.CREATION_MODTIMESTAMP] = 0,
      });
      result.Add(GameCard.FromProperties(props, instance, null));
    }
    return result;
  }

  /// <summary>
  /// Static hook on <c>HandleRevealedCard</c> that buffers the revealed card's
  /// player index and CTN for the next <see cref="ProcessGameplayStatus"/> tick.
  /// <para>
  /// MTGO's message pump processes <c>HandleRevealedCard</c> before the paired
  /// <c>HandleGamePlayStatus</c> on the same thread, but both hook callbacks are
  /// dispatched to <see cref="SyncThread"/> without a group ID — a concurrent
  /// thread pool with no ordering guarantee. In practice the reveal IPC almost
  /// always arrives first (the messages fire microseconds apart), but on rare
  /// occasions the reveal may land one snapshot tick late. This is acceptable
  /// for a display-only overlay; no game data is lost.
  /// </para>
  /// </summary>
  private static readonly EventHookProxy<int, int>
    s_handleRevealedCard = new(
      "WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game",
      "HandleRevealedCard",
      new((instance, args) =>
      {
        int gameId = (int)instance.Id;
        if (!s_activeProcessors.TryGetValue(gameId, out var processor)) return null;

        try
        {
          dynamic message = args[0];
          int playerNum = (int)(uint)message.PlayerNum;
          int ctn       = (int)(uint)message.CardID;

          // Name is resolved from ThingElements later via GameCard.Name — skip
          // the expensive m_digitalThings scan here since the hook fires on the
          // SyncThread (serialised with HandleGamePlayStatus) and must be fast.
          processor._pendingReveals.Enqueue(new RevealedCardItem(playerNum, ctn, ""));
        }
        catch (Exception ex)
        {
          Log.Warning(ex, "[GameProcessor] HandleRevealedCard hook failed for game {Id}", gameId);
        }

        return (gameId, 0);
      })
    );


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
        else
        {
          // Buffer for replay when Activate() is called.
          var queue = s_bufferedHooks.GetOrAdd(gameId,
            _ => new ConcurrentQueue<(object, object, (uint, DateTime))>());
          queue.Enqueue((instance, message, ts));
        }

        return (gameId, ts);
      })
    );
}
