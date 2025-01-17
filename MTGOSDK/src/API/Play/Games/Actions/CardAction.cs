/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a game action performed by a card.
/// </summary>
public class CardAction(dynamic cardAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj => Bind<ICardAction>(cardAction);

  //
  // ICardAction wrapper properties
  //

  /// <summary>
  /// The source card for the action.
  /// </summary>
  public GameCard Card => new(@base.Card);

  public IList<TargetSet> Targets =>
    Map<IList, TargetSet>(@base.Targets);

  public bool IsManaAbility => @base.IsManaAbility;
}
