/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Reconciles finalized target commitments and manages the full action lifecycle.
/// Aggregates actions, handles Undo/Cancel, and finalizes based on game prompts.
/// </summary>
public sealed class ActionProcessor : IGameStateProcessor, IDisposable
{
  private sealed class PendingCommitment(CardAction action, dynamic? message, int cardId)
  {
    public CardAction Action { get; } = action;
    public dynamic? Message { get; } = message;

    /// <summary>
    /// The card's Id captured at action receive time (while still in Hand).
    /// When the card moves to Stack, this becomes the Stack card's SourceId.
    /// </summary>
    public int CardId { get; } = cardId;
  }

  //
  // Target reconciliation state
  //

  private readonly List<PendingCommitment> _commitments = new();

  /// <summary>
  /// Maps card Hand Id → flat list of target IDs extracted from action messages.
  /// Populated during Process() when commitments are reconciled, consumed by
  /// BackfillPendingTargets() when the card materializes on the Stack (where
  /// its SourceId equals the Hand Id).
  /// </summary>
  private readonly Dictionary<int, List<int>> _resolvedTargetIds = new();

  //
  // Action lifecycle state
  //

  private readonly List<GameAction> _pendingActions = new();
  private readonly List<GameAction> _finalizedActions = new();

  private Game? _game;
  private int _gameId;
  private readonly object _lock = new();

  //
  // Action collection and finalization
  //

  public void Initialize(Game game)
  {
    _game = game;
    _gameId = game.Id;
    s_gameActionPerformed += OnGameActionReceived;
  }

  private void OnGameActionReceived(Game game, GameAction action)
  {
    int incomingId = Try(() => game.Id, () => -1);
    if (incomingId != _gameId)
      return;

    lock (_lock)
    {
      // Undo the last action on the stack.
      if (action is UndoAction)
      {
        // Check pending actions first, then finalized actions.
        if (_pendingActions.Count > 0)
        {
          _pendingActions.RemoveAt(_pendingActions.Count - 1);
        }
        else if (_finalizedActions.Count > 0)
        {
          _finalizedActions.RemoveAt(_finalizedActions.Count - 1);
        }
        return;
      }

      // Cancel all actions on the stack from the current interaction timestamp.
      if (action is PrimitiveAction primitiveAction && primitiveAction.Name == "Cancel")
      {
        _pendingActions.RemoveAll(a => a.Timestamp == action.Timestamp);
        return;
      }

      // Queue for target reconciliation if it's a card action with a message.
      // Retain the pre-execution card identity for the later stack-card backfill.
      if (action is CardAction cardAction)
      {
        int cardId = Try(() => cardAction.Card.Id, () => -1);
        _commitments.Add(new PendingCommitment(cardAction, cardAction.Message, cardId));
      }

      // Push the action onto the stack to await prompt finalization.
      _pendingActions.Add(action);
    }
  }

  public void Process(GameContext context)
  {
    List<PendingCommitment> commitmentsBatch;
    List<GameAction> finalizedBatch;

    lock (_lock)
    {
      // TurnStep finalizes pending actions. InteractState leaves them available
      // for Undo while the client is still building the action.
      if (context.Current.StateType == StateElementType.TurnStep && _pendingActions.Count > 0)
      {
        // Sort by timestamp to ensure correct order despite concurrent
        // callback delivery through ChannelScheduler.
        _pendingActions.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        _finalizedActions.AddRange(_pendingActions);
        _pendingActions.Clear();
      }

      if (_commitments.Count == 0 && _finalizedActions.Count == 0)
        return;

      commitmentsBatch = new List<PendingCommitment>(_commitments);
      _commitments.Clear();

      finalizedBatch = new List<GameAction>(_finalizedActions);
      _finalizedActions.Clear();
    }

    // Reconcile targets for all commitments.
    foreach (var commitment in commitmentsBatch)
    {
      try
      {
        ReconcileAction(context, commitment.Action, commitment.Message);

        // Store resolved target IDs keyed by Hand Id for later backfill.
        StoreResolvedTargets(commitment.CardId, commitment.Message);
      }
      catch (Exception e)
      {
        Log.Error(e, "Target reconciliation failed for action {0}.", commitment.Action.Name);
      }
    }

    // Dispatch finalized actions through centralized bus.
    foreach (var action in finalizedBatch)
    {
      context.Emit(new ActionFinalizedEventArgs(action));
    }
  }

  //
  // Target reconciliation
  //

  /// <summary>
  /// Resolves action targets against the current snapshot, using the action
  /// message or target wrappers retained by target-change notifications.
  /// </summary>
  private void ReconcileAction(
    GameContext context,
    CardAction action,
    dynamic? message)
  {
    if (TryReconcileFromMessage(context, action, message, _game))
    {
      if (!action.RequiresTargets || action.IsTargetsSet)
      {
        action.TriggerTargetsSet();
      }
      return;
    }

    // Fallback path: use target wrappers captured by target events.
    foreach (var targetSet in action.Targets)
    {
      var resolved = new List<Targetable>();

      // Match captured target identities against the current snapshot.
      foreach (var target in targetSet.CurrentTargets)
      {
        int id = target.Id;
        if (context.Current.Cards.TryGetValue(id, out var card))
        {
          resolved.Add(new Targetable(card));
        }
        else if (context.Current.HiddenCards.TryGetValue(id, out var hidden))
        {
          resolved.Add(new Targetable(hidden));
        }
        else
        {
          // Keep the existing one if we can't find a better match
          resolved.Add(target);
        }
      }

      targetSet.UpdateTargets(resolved);
    }

    if (!action.RequiresTargets || action.IsTargetsSet)
    {
      action.TriggerTargetsSet();
    }
  }

  private static bool TryReconcileFromMessage(
    GameContext context,
    CardAction action,
    dynamic? message,
    Game? game)
  {
    if (message == null)
      return false;

    int[]? rawData = Try<int[]>(() => (int[])message.Targets);
    if (rawData == null || rawData.Length == 0)
      return false;

    var slots = SplitTargetSlots(rawData);

    // Native FLAGS_HAS_DIVIDE_TARGETS (2) appends distribution values.
    bool isDistributed = Try<bool>(() => ((uint)message.Flags & 2) != 0);
    var allTargetIds = slots.SelectMany(s => s).ToList();
    var values = new List<int>();
    if (isDistributed && slots.Count > 0)
    {
      int valueCount = allTargetIds.Count;
      int tailStart = rawData.Length - valueCount;
      if (tailStart >= 0)
      {
        for (int i = tailStart; i < rawData.Length; i++)
        {
          values.Add(rawData[i]);
        }

        var lastSlot = slots[^1];
        int valuesInLastSlot = Math.Min(lastSlot.Count, valueCount);
        if (valuesInLastSlot > 0)
        {
          lastSlot.RemoveRange(lastSlot.Count - valuesInLastSlot, valuesInLastSlot);
        }
      }
    }

    int valIdx = 0;
    for (int i = 0; i < Math.Min(slots.Count, action.Targets.Count); i++)
    {
      var sdkTargetSet = action.Targets[i];
      var resolvedTargets = new List<Targetable>();

      foreach (int id in slots[i])
      {
        if (id == -1)
          continue;

        Targetable resolved = ResolveId(context, id, game);
        if (isDistributed && valIdx < values.Count)
        {
          SetTargetInformation(resolved, values[valIdx++].ToString());
        }
        resolvedTargets.Add(resolved);
      }

      sdkTargetSet.UpdateTargets(resolvedTargets);
    }

    return true;
  }

  // MTGO separates target sets with -2. Empty slots must retain their position.
  private static List<List<int>> SplitTargetSlots(int[] targets)
  {
    var slots = new List<List<int>>();
    var current = new List<int>();
    foreach (int target in targets)
    {
      if (target == -2)
      {
        slots.Add(current);
        current = new List<int>();
      }
      else
      {
        current.Add(target);
      }
    }
    slots.Add(current);
    return slots;
  }

  private static Targetable ResolveId(GameContext context, int id, Game? game)
  {
    if (context.Current.Cards.TryGetValue(id, out var card))
      return new Targetable(card);

    // Player IDs are small, fixed identifiers in the MTGO data model.
    if (id >= 0 && id < 6 && game != null)
    {
      var player = game.Players.FirstOrDefault(p => (int)Unbind(p).Id == id);
      if (player != null)
        return new Targetable(player);
    }

    if (context.Current.HiddenCards.TryGetValue(id, out var hidden))
      return new Targetable(hidden);

    var ghostProperties = new PropertyContainer(new Dictionary<MagicProperty, dynamic>
    {
      { MagicProperty.THINGNUMBER, id }
    });
    var references = context.Processor?.References
      ?? (game is null ? null : GameProcessor.ForGame(game).References);
    var ghost = references != null
      ? references.CreatePlaceholder(ghostProperties)
      : new GameReferenceResolver(null).CreatePlaceholder(ghostProperties);
    return new Targetable(Unbind(ghost));
  }

  /// <summary>
  /// Extracts target IDs from a server action message and stores them in
  /// <see cref="_resolvedTargetIds"/> keyed by the card's Hand Id.
  /// </summary>
  private void StoreResolvedTargets(int cardId, dynamic? message)
  {
    if (message == null)
      return;

    int[]? rawData = Try<int[]>(() => (int[])message.Targets);
    if (rawData == null || rawData.Length == 0)
      return;

    var slots = SplitTargetSlots(rawData);

    // Strip distribution values when native FLAGS_HAS_DIVIDE_TARGETS (2) is set.
    bool isDistributed = Try<bool>(() => ((uint)message.Flags & 2) != 0);
    var targetIds = slots.SelectMany(s => s).Where(id => id >= 0).ToList();
    if (isDistributed && targetIds.Count > 0)
    {
      int tailCount = targetIds.Count;
      int tailStart = rawData.Length - tailCount;
      if (tailStart >= 0)
      {
        targetIds = rawData.Take(tailStart)
          .Where(v => v >= 0)
          .ToList();
      }
    }
    if (targetIds.Count <= 1)
      return;

    if (cardId > 0)
      _resolvedTargetIds[cardId] = targetIds;
  }

  /// <summary>
  /// Adds resolved target associations and local properties to captured card adapters.
  /// Called by GameProcessor before PropertyChangeProcessor runs, so that
  /// card change diffs see the complete target list.
  /// </summary>
  /// <remarks>
  /// The action message can provide additional targets before its stack card
  /// appears in a gameplay snapshot. Native captured properties stay unchanged.
  /// </remarks>
  public void BackfillPendingTargets(GameContext context)
  {
    if (_resolvedTargetIds.Count == 0)
      return;

    // Try to match resolved targets against Stack cards in the current snapshot.
    // The key is the card's Hand Id; on the Stack, SourceId == Hand Id.
    var consumed = new List<int>();
    foreach (var (handId, targetIds) in _resolvedTargetIds)
    {
      GameCard? card = null;
      foreach (var c in context.Current.Cards.Values)
      {
        if (c.Zone?.Name != "Stack")
          continue;
        if (c.SourceId == handId || c.Id == handId)
        {
          card = c;
          break;
        }
      }
      if (card == null)
        continue;

      if (Unbind(card) is not GameCardPartial partial)
        continue;

      for (int i = 0; i < targetIds.Count; i++)
      {
        var prop = (MagicProperty)((uint)MagicProperty.ACTION_TARGETID0 + (uint)i);
        partial.Properties.SetLocal(prop, targetIds[i]);
      }

      partial.BackfillTargets(targetIds);
      consumed.Add(handId);
    }

    foreach (int id in consumed)
      _resolvedTargetIds.Remove(id);
  }

  private static void SetTargetInformation(Targetable target, string info)
  {
    object obj = target.ToGameObject();
    if (obj is GameCard card && GameCard.Unbind(card) is GameCardPartial partial)
    {
      partial.TargetInformation = info;
    }
  }

  //
  // Lifetime and native action hook
  //

  public void Dispose()
  {
    s_gameActionPerformed -= OnGameActionReceived;
  }

  /// <summary>
  /// Ensures the static <c>GameActionPerformed</c> hook is installed
  /// so that action detection is active.
  /// </summary>
  public static void EnsureHookInitialized() =>
    s_gameActionPerformed.EnsureInitialize();

  /// <summary>
  /// Event triggered when a game action in any active game is performed.
  /// </summary>
  private static EventHookProxy<Game, GameAction> s_gameActionPerformed =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.Actions.GameAction>(),
      "Execute",
      new((instance, args) =>
      {
        try
        {
          if (args == null || args.Length == 0)
          {
            return null;
          }

          dynamic rawAction = instance;
          dynamic gameArg = args[0];

          GameAction action = GameAction.GameActionFactory(rawAction);
          if (action == null || action.IsLocal)
          {
            return null;
          }

          Game game = new(gameArg);
          if (action.Timestamp == 0)
          {
            uint promptTimestamp = game.Prompt?.Timestamp ?? 0;
            if (promptTimestamp != 0)
            {
              action.SetTimestamp(promptTimestamp);
            }
          }

          // Capture client-side execution timestamp from the hook instance.
          action.SetClientTimestamp((DateTime)instance.__timestamp);

          // Enable target event capture for this action instance. This tracks
          // target selection changes across collection reset/add cycles.
          if (action is CardAction cardAction)
          {
            cardAction.UseTargetEvents();
            cardAction.Message = Try<dynamic>(() => Unbind(cardAction).Message);
          }

          return (game, action);
        }
        catch (Exception e)
        {
          Log.Error(e, "Failed to capture a performed game action.");
          return null;
        }
      })
    );
}
