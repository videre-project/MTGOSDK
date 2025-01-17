/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public class DistributingCardAction(dynamic cardAction)
    // We override the base instance with the IDistributingCardAction interface.
    : CardAction(null)
{
  /// <summary>
  /// Stores an internal reference to the IDistributingCardAction object.
  /// </summary>
  internal override dynamic obj => Bind<IDistributingCardAction>(cardAction);

  //
  // IDistributingCardAction wrapper properties
  //

  public bool AreTargetsEditable => @base.AreTargetsEditable;

  public int MinimumTotal => @base.MinimumTotal;

  public int MaximumTotal => @base.MaximumTotal;
}
