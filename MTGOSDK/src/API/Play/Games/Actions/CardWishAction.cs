/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class CardWishAction(dynamic cardWishAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the ICardWishAction object.
  /// </summary>
  internal override dynamic obj => Bind<ICardWishAction>(cardWishAction);

  //
  // ICardWishAction wrapper properties
  //

  public Card WishedCard => new(@base.WishedCard);
}
