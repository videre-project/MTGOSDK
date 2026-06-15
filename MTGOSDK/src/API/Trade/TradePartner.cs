/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Trade;


namespace MTGOSDK.API.Trade;

public class TradePartner(dynamic tradePartner)
    : DLRWrapper<IPreviousTradePartner>
{
  /// <summary>
  /// Stores an internal reference to the IPreviousTradePartner object.
  /// </summary>
  internal override dynamic obj => Bind<IPreviousTradePartner>(tradePartner);

  //
  // IPreviousTradePartner wrapper properties
  //

  [NonSerializable]
  public User Poster => new(this.PosterName);

  public string PosterName => @base.User.Name;

  public DateTime LastTradeTime => @base.LastTradeTime;
}
