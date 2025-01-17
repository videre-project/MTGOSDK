/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class OrderingAction(dynamic orderingAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the IOrderingAction object.
  /// </summary>
  internal override dynamic obj => Bind<IOrderingAction>(orderingAction);

  //
  // IOrderingAction wrapper properties
  //

  public GameCard Source => new(@base.Source);

  public IList<GameCard> OrderedTargets =>
    Map<IList, GameCard>(@base.OrderedTargets);
}
