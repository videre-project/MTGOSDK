/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Chat;
using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade.Enums;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Trade;


namespace MTGOSDK.API.Trade;

public class TradeEscrow(dynamic tradeEscrow) : DLRWrapper<ITradeEscrow>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ITradeEscrow);

  /// <summary>
  /// Stores an internal reference to the ITradeEscrow object.
  /// </summary>
  internal override dynamic obj => tradeEscrow;

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
  /// Whether this escrow is with another player rather than an MTGO service.
  /// </summary>
  public bool IsPlayerTrade => Try<bool>(() => @base.RemoteParticipant != null);

  /// <summary>
  /// The other player's login ID, or null for non-player escrows.
  /// </summary>
  public int? TradePartnerId => Try<int?>(() => @base.RemoteParticipant?.Id);

  /// <summary>
  /// The other user being traded with.
  /// </summary>
  [NonSerializable]
  public User? TradePartner => TradePartnerId is int id ? new(id) : null;

  /// <summary>
  /// The display name of the other user being traded with.
  /// </summary>
  public string? TradePartnerName => Try<string?>(() => @base.RemoteParticipant?.Name);

  /// <summary>
  /// The other user's collection.
  /// </summary>
  [NonSerializable]
  public ItemCollection PartnerCollection => new(@base.RemoteParticipantCollection);

  /// <summary>
  /// The items traded by the other user.
  /// </summary>
  public ItemCollection PartnerTradedItems => new(@base.ItemsFromRemoteParticipant);

  /// <summary>
  /// The current user's collection.
  /// </summary>
  [NonSerializable]
  public ItemCollection Collection => new(@base.LocalUserCollection);

  /// <summary>
  /// The items traded by the current user.
  /// </summary>
  public ItemCollection TradedItems => new(@base.ItemsFromLocalUser);

  /// <summary>
  /// The current binder used for the trade.
  /// </summary>
  [NonSerializable]
  public Binder ActiveBinder => new(@base.ActiveBinder);

  /// <summary>
  /// A collection of all tradeable items in the active binder.
  /// </summary>
  [NonSerializable]
  public IEnumerable<CardQuantityPair> TradeableItems =>
    ItemCollection.SerializeCardQuantityPairs(
      ((DynamicRemoteObject)Unbind(ActiveBinder).Items)
        .Filter<ICardCollectionItem>(
          item => item.CardDefinition.IsTradable == true)
        .Filter<ICardCollectionItem>(
          item => item.LockedQuantity < item.Quantity));

  [NonSerializable]
  public Channel? ChatChannel =>
    TradePartnerId is int id ? ChannelManager.GetPrivateChannel(id) : null;

  //
  // ITradeEscrow wrapper events
  //

  /// <summary>
  /// Event raised when the current user updates items in the trade escrow.
  /// </summary>
  public EventHookWrapper<IList<CardQuantityPair>> OnSendTradeItemUpdate =
    new(SendTradeItemUpdate, new((s,_) => s.Id == tradeEscrow.EscrowId));

  /// <summary>
  /// Event raised when the other user updates items in the trade escrow.
  /// </summary>
  public EventHookWrapper<IList<CardQuantityPair>> OnReceiveTradeItemUpdate =
    new(ReceiveTradeItemUpdate, new((s,_) => s.Id == tradeEscrow.EscrowId));

  //
  // ITradeEscrow static events
  //

  /// <summary>
  /// Event raised when the current user updates items in the trade escrow.
  /// </summary>
  public static EventHookProxy<TradeEscrow, IList<CardQuantityPair>> SendTradeItemUpdate =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Trade.TradeProcessor.TradeEscrow.NegotiateBaseState>(),
      "SendTradeItemUpdate",
      new((instance, args) =>
      {
        var items = ItemCollection.SerializeCollectionItems(args[0]);
        var trade = new TradeEscrow(args[1]);

        // Set event timestamp for correlation.
        var timestamp = instance.__timestamp;
        Unbind(trade).__timestamp = timestamp;

        return (trade, items);
      })
    );

  /// <summary>
  /// Event raised when the other user updates items in the trade escrow.
  /// </summary>
  public static EventHookProxy<TradeEscrow, IList<CardQuantityPair>> ReceiveTradeItemUpdate =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Trade.TradeProcessor.TradeEscrow.NegotiateBaseState>(),
      "HandleTradeItemUpdate",
      new((instance, args) =>
      {
        var msg = args[0]; // TradeItemUpdateMessage
        var items = ItemCollection.SerializeCollectionItems(msg.UpdatedItems.Items);
        var trade = new TradeEscrow(args[1]);

        // Set event timestamp for correlation.
        var timestamp = instance.__timestamp;
        Unbind(trade).__timestamp = timestamp;

        return (trade, items);
      })
    );
}
