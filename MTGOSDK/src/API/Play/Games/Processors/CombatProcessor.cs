/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.Partials;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Cross-links combat orders (attacking/blocking) on GameCardPartial snapshots.
/// Must run before PropertyChangeTracker so that combat order changes are
/// detected via GameCardPartial.Equals.
/// </summary>
public sealed class CombatProcessor : IGameStateProcessor
{
  public void Initialize(Game game) { }

  public void Process(GameContext context)
  {
    // Clear all combat orders first
    foreach (var card in context.Current.Cards.Values)
    {
      if ((object)GameCard.Unbind(card) is not GameCardPartial partial) continue;
      partial.AttackingOrders.Clear();
      partial.BlockingOrders.Clear();
    }

    // Populate attackers from ATTACKING* properties
    foreach (var card in context.Current.Cards.Values)
    {
      if (!card.IsAttacking) continue;
      if ((object)GameCard.Unbind(card) is not GameCardPartial partial) continue;

      var attackingTids = new Dictionary<int, int>();
      var attackingSorts = new Dictionary<int, int>();

      foreach (var (prop, val) in partial.Properties.AllProperties)
      {
        if (val is not int intVal) continue;

        string name = partial.Properties.GetPropertyName(prop);
        if (TryParseIndexedProperty(name, "ATTACKING_SORT", out int sIdx))
          attackingSorts[sIdx] = intVal;
        else if (TryParseIndexedProperty(name, "ATTACKING_ID", out int idIdx))
          attackingTids[idIdx] = intVal;
        else if (TryParseIndexedProperty(name, "ATTACKING", out int aIdx))
          attackingTids[aIdx] = intVal;
      }

      for (int i = 0; i < 256; i++)
      {
        if (!attackingTids.TryGetValue(i, out int defenderTid)) break;
        if (!context.Current.Cards.TryGetValue(defenderTid, out var defender)) continue;

        int sortOrder = attackingSorts.TryGetValue(i, out int s) ? s : i + 1;
        partial.AttackingOrders.Add(new GameCard.OrderedCombatParticipant(sortOrder, defender));
      }
    }

    // Populate blockers from BLOCKING* properties and mirror to attackers
    foreach (var card in context.Current.Cards.Values)
    {
      if (!card.IsBlocking) continue;
      if ((object)GameCard.Unbind(card) is not GameCardPartial partial) continue;

      var blockingTids = new Dictionary<int, int>();
      var blockingSorts = new Dictionary<int, int>();

      foreach (var (prop, val) in partial.Properties.AllProperties)
      {
        if (val is not int intVal) continue;

        string name = partial.Properties.GetPropertyName(prop);
        if (TryParseIndexedProperty(name, "BLOCKING_SORT", out int sIdx))
          blockingSorts[sIdx] = intVal;
        else if (TryParseIndexedProperty(name, "BLOCKING_ID", out int idIdx))
          blockingTids[idIdx] = intVal;
        else if (TryParseIndexedProperty(name, "BLOCKING", out int bIdx))
          blockingTids[bIdx] = intVal;
      }

      var orders = new List<GameCard.OrderedCombatParticipant>();
      for (int i = 0; i < 256; i++)
      {
        if (!blockingTids.TryGetValue(i, out int attackerTid)) break;
        if (!context.Current.Cards.TryGetValue(attackerTid, out var attacker)) continue;

        int sortOrder = blockingSorts.TryGetValue(i, out int s) ? s : 1;
        orders.Add(new GameCard.OrderedCombatParticipant(sortOrder, attacker));

        // Mirror to attacker's list
        if ((object)GameCard.Unbind(attacker) is GameCardPartial attackerPartial)
        {
          bool exists = attackerPartial.AttackingOrders.Any(p =>
            p.Order == i + 1 && p.Target?.Id == card.Id);
          if (!exists)
          {
            attackerPartial.AttackingOrders.Add(
              new GameCard.OrderedCombatParticipant(i + 1, card));
          }
        }
      }
      partial.BlockingOrders.AddRange(orders);
    }
  }

  private static bool TryParseIndexedProperty(
    string propertyName,
    string prefix,
    out int index)
  {
    index = default;
    return propertyName.StartsWith(prefix, System.StringComparison.Ordinal)
      && int.TryParse(propertyName[prefix.Length..], out index);
  }
}
