/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.Partials;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class CombatDamageAssignmentAction(
    dynamic combatDamageAssignmentAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the ICombatDamageAssignmentAction object.
  /// </summary>
  internal override dynamic obj =>
    combatDamageAssignmentAction is DamageAssignmentPartial partial
      ? partial
      : Bind<ICombatDamageAssignmentAction>(combatDamageAssignmentAction);

  //
  // ICombatDamageAssignmentAction wrapper properties
  //

  public GameCard Source =>
    combatDamageAssignmentAction is DamageAssignmentPartial partial
      ? new GameCard(partial.SourceCard)
      : new(@base.Source);

  public IList<Distribution> Distributions =>
    combatDamageAssignmentAction is DamageAssignmentPartial partial
      ? partial.DistributionPartials
          .Select(d => new Distribution(d))
          .ToList<Distribution>()
      : Map<IList, Distribution>(Unbind(this).Distributions);

  public int MinimumTotal => @base.MinimumTotal;

  public int MaximumTotal => @base.MaximumTotal;
}
