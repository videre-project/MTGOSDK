/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Dynamic;
using System.Linq;


namespace MTGOSDK.API.Play.Games.Processors.Partials;

/// <summary>
/// A local DynamicObject that backs a CombatDamageAssignmentAction from
/// snapshot data parsed from a DamageAssignment state element.
/// </summary>
public class DamageAssignmentPartial(
  int damagingThingId,
  int damageToDeal,
  List<DistributionPartial> distributions) : DynamicObject
{
  public int DamagingThingId => damagingThingId;

  /// <summary>
  /// The resolved source card partial. Set by GameProcessor after construction.
  /// </summary>
  public dynamic? SourceCard { get; set; }

  /// <summary>
  /// Access to the raw distribution partials for the wrapper's partial path.
  /// </summary>
  public List<DistributionPartial> DistributionPartials => distributions;

  public override bool TryGetMember(GetMemberBinder binder, out object? result)
  {
    switch (binder.Name)
    {
      case "Source":
        result = SourceCard;
        return true;

      case "Distributions":
        result = (IList<object>)distributions.Cast<object>().ToList();
        return true;

      case "MinimumTotal":
        result = damageToDeal;
        return true;

      case "MaximumTotal":
        result = damageToDeal;
        return true;

      // GameAction base properties accessed via Unbind(this)
      case "Name":
        result = "Combat Damage Assignment";
        return true;

      case "ActionFlags":
      case "Timestamp":
      case "HotKey":
        result = (uint)0;
        return true;

      case "ActionType":
        result = 0; // DistributeAmongTargets
        return true;

      default:
        result = null;
        return false;
    }
  }
}

/// <summary>
/// A local DynamicObject that backs a Distribution from snapshot data.
/// </summary>
public class DistributionPartial(
  int thingId,
  int amount,
  int minimum,
  int maximum) : DynamicObject
{
  public int ThingId => thingId;

  /// <summary>
  /// The resolved target (card partial or player partial). Set by GameProcessor.
  /// </summary>
  public dynamic? TargetObject { get; set; }

  public override bool TryGetMember(GetMemberBinder binder, out object? result)
  {
    switch (binder.Name)
    {
      case "Target":
        result = TargetObject;
        return true;

      case "Value":
        result = amount;
        return true;

      case "Minimum":
        result = minimum;
        return true;

      case "Maximum":
        result = maximum;
        return true;

      case "Id":
        result = thingId;
        return true;

      default:
        result = null;
        return false;
    }
  }
}
