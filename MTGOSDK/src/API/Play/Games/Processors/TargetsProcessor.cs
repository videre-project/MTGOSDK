/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.Core.Reflection.DLRWrapper;

namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Synchronizes real-time target selection state across all active game actions.
/// </summary>
public sealed class TargetsProcessor : IGameStateProcessor
{
  private Game? _game;

  public void Initialize(Game game)
  {
    _game = game;
  }

  public void Process(GameContext context)
  {
    if (_game == null) return;
    // Requires game instance to reach active actions; context may include them later.
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
}
