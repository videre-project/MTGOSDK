/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.API.Events;
using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade.Enums;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Trade;

using CollectionItem = MTGOSDK.API.Collection.CollectionItem<dynamic>;


namespace MTGOSDK.API.Trade;

public class TradeEscrow(dynamic tradeEscow) : DLRWrapper<ITradeEscrow>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ITradeEscrow);

  /// <summary>
  /// Stores an internal reference to the ITradeEscrow object.
  /// </summary>
  internal override dynamic obj => tradeEscow;

  //
  // ITradeEscrow wrapper properties
  //

  public int Id => @base.EscrowId;

  public Guid Token => Cast<Guid>(@base.EscrowToken);

  /// <summary>
  /// The current state of the trade escrow.
  /// </summary>
  public TradeState State => Cast<TradeState>(@base.CurrentState);

  /// <summary>
  /// The final state of the trade escrow.
  /// </summary>
  public TradeFinalState FinalState =>
    Cast<TradeFinalState>(@base.FinalTradeState);

  /// <summary>
  /// Whether the trade was accepted.
  /// </summary>
  public bool IsAccepted => @base.IsGrant;

  /// <summary>
  /// The other user being traded with.
  /// </summary>
  public User TradePartner => new(@base.RemoteParticipant.Name);

  /// <summary>
  /// The other user's collection.
  /// </summary>
  public ItemCollection PartnerCollection => new(@base.RemoteParticipantCollection);

  /// <summary>
  /// The items traded by the other user.
  /// </summary>
  public ItemCollection PartnerTradedItems => new(@base.ItemsFromRemoteParticipant);

  /// <summary>
  /// The current user's collection.
  /// </summary>
  public ItemCollection Collection => new(@base.LocalUserCollection);

  /// <summary>
  /// The items traded by the current user.
  /// </summary>
  public ItemCollection TradedItems => new(@base.ItemsFromLocalUser);

  /// <summary>
  /// The current binder used for the trade.
  /// </summary>
  public Binder ActiveBinder => new(@base.ActiveBinder);

  /// <summary>
  /// A collection of all tradeable items in the active binder.
  /// </summary>
  public IEnumerable<CollectionItem> TradeableItems =>
    Map<CollectionItem>(
      Filter(ActiveBinder.Items,
             item => Try<bool>(item?.Card.IsTradeable) &&
                     item.Quantity - item.LockedQuantity > 0));

  //
  // ITradeEscrow wrapper events
  //

  public EventProxy<TradeStateChangedEventArgs> TradeStateChanged =
    new(/* ITradeEscrow */ tradeEscow, nameof(TradeStateChanged));

  public EventProxy<TradeErrorEventArgs> TradeError =
    new(/* ITradeEscrow */ tradeEscow, nameof(TradeError));
}
