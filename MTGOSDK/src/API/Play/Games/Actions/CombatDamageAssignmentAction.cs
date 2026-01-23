/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class CombatDamageAssignmentAction(
    dynamic combatDamageAssignmentAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj =>
    Bind<ICombatDamageAssignmentAction>(combatDamageAssignmentAction);

  //
  // ICardSelectorAction wrapper properties
  //

  public GameCard Source => new(@base.Source);

  public IList<Distribution> Distributions =>
    Map<IList, Distribution>(Unbind(this).Distributions);

  public int MinimumTotal => @base.MinimumTotal;

  public int MaximumTotal => @base.MaximumTotal;
}
