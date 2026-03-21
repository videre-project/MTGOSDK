/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Reconciles finalized target commitments and manages the full action lifecycle.
/// Aggregates actions, handles Undo/Cancel, and finalizes based on game prompts.
/// </summary>
public sealed class ActionProcessor : IGameStateProcessor
{
  private sealed class PendingCommitment(CardAction action, dynamic? message)
  {
    public CardAction Action { get; } = action;
    public dynamic? Message { get; } = message;
  }

  // Target reconciliation
  private readonly List<PendingCommitment> _commitments = new();
  
  // Action lifecycle
  private readonly List<GameAction> _pendingActions = new();
  private readonly List<GameAction> _finalizedActions = new();
  
  private Game? _game;
  private readonly object _lock = new();

  public void Initialize(Game game)
  {
    _game = game;
    s_GameActionPerformed += OnGameActionReceived;
  }

  private void OnGameActionReceived(Game game, GameAction action)
  {
    if (game.Id != _game.Id) return;

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
      if (action is CardAction cardAction)
      {
        _commitments.Add(new PendingCommitment(cardAction, cardAction.Message));
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
      //
      // Finalize pending actions only when TurnStep fires.
      //
      // InteractState means we're still building/resolving actions,
      // so undo can still remove them from the pending stack.
      //
      if (context.Current.StateType == StateElementType.TurnStep && _pendingActions.Count > 0)
      {
        // Sort by timestamp to ensure correct order despite concurrent
        // callback execution from ChannelScheduler's worker threads.
        _pendingActions.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        _finalizedActions.AddRange(_pendingActions);
        _pendingActions.Clear();
      }

      if (_commitments.Count == 0 && _finalizedActions.Count == 0) return;
      
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
        ReconcileAction(
          context,
          commitment.Action,
          commitment.Message,
          _game);
      }
      catch (Exception e)
      {
        Log.Error("ActionProcessor target reconciliation failed for {0}: {1}", commitment.Action.Name, e.Message);
        Log.Debug(e.ToString());
      }
    }

    // Dispatch finalized actions through centralized bus.
    foreach (var action in finalizedBatch)
    {
      context.Emit(new ActionFinalizedEventArgs(action));
    }
  }

  /// <summary>
  /// Resolves targets for a specific action against either the server action
  /// message payload or the live action target state.
  /// </summary>
  internal void ReconcileAction(
    GameContext context,
    CardAction action,
    dynamic? message = null,
    Game? game = null)
  {
    if (TryReconcileFromMessage(context, action, message, game ?? _game))
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
      
      // Pull IDs from CurrentTargets; assume Targetable.Id is stable.
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

  private bool TryReconcileFromMessage(
    GameContext context,
    CardAction action,
    dynamic? message,
    Game? game)
  {
    if (message == null) return false;

    int[]? rawData = Try<int[]>(() => (int[])message.Targets);
    if (rawData == null || rawData.Length == 0) return false;

    // Split target slots by sentinel 0xFFFFFFFE (-2).
    var slots = new List<List<int>>();
    var currentSlot = new List<int>();
    foreach (int val in rawData)
    {
      if (val == -2)
      {
        slots.Add(currentSlot);
        currentSlot = new List<int>();
      }
      else
      {
        currentSlot.Add(val);
      }
    }
    slots.Add(currentSlot);

    // If HAS_DIVIDE_TARGETS is set, the tail contains distribution values.
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

        var lastSlot = slots.Last();
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
        if (id == -1) continue;

        Targetable resolved = ResolveId(context, id, game);
        if (resolved != null)
        {
          if (isDistributed && valIdx < values.Count)
          {
            SetTargetInformation(resolved, values[valIdx++].ToString());
          }
          resolvedTargets.Add(resolved);
        }
      }

      sdkTargetSet.UpdateTargets(resolvedTargets);
    }

    return true;
  }

  private Targetable ResolveId(GameContext context, int id, Game? game)
  {
    if (context.Current.Cards.TryGetValue(id, out var card))
      return new Targetable(card);

    // Player IDs are small, fixed identifiers in the MTGO data model.
    if (id >= 0 && id < 6 && game != null)
    {
      var player = game.Players.FirstOrDefault(p => (int)Unbind(p).Id == id);
      if (player != null) return new Targetable(player);
    }

    if (context.Current.HiddenCards.TryGetValue(id, out var hidden))
      return new Targetable(hidden);

    var ghostProperties = new PropertyContainer(new Dictionary<MagicProperty, dynamic>
    {
      { MagicProperty.THINGNUMBER, id }
    });
    return new Targetable(new GameCardPartial(ghostProperties, game != null ? Unbind(game) : null));
  }

  private static void SetTargetInformation(Targetable target, string info)
  {
    dynamic obj = target.ToGameObject();
    if (obj is GameCard card && GameCard.Unbind(card) is GameCardPartial partial)
    {
      partial.TargetInformation = info;
    }
  }

  public void Dispose()
  {
    s_GameActionPerformed -= OnGameActionReceived;
  }

  //
  // 
  //
  
  /// <summary>
  /// Event triggered when a game action in any active game is performed.
  /// </summary>
  private static EventHookProxy<Game, GameAction> s_GameActionPerformed =
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
          Log.Error("GameActionPerformed hook error: {0}", e);
          return null;
        }
      })
    );
}
