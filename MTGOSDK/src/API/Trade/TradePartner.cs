/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.ComponentModel;

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Trade;


namespace MTGOSDK.API.Trade;

public class TradePartner(dynamic tradePartner)
    : DLRWrapper<IPreviousTradePartner>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IPreviousTradePartner);

  /// <summary>
  /// Stores an internal reference to the IPreviousTradePartner object.
  /// </summary>
  internal override dynamic obj => tradePartner;

  //
  // IPreviousTradePartner wrapper properties
  //

  public User Poster => new(@base.User.Name);

	public DateTime LastTradeTime => @base.LastTradeTime;

  //
  // INotifyPropertyChanged wrapper events
  //

  public EventProxy<PropertyChangedEventArgs> PropertyChanged =
    new(/* IPreviousTradePartner */ tradePartner, nameof(PropertyChanged));
}
