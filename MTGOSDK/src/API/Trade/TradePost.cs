/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade.Enums;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Trade;


namespace MTGOSDK.API.Trade;

public class TradePost(dynamic tradePost) : DLRWrapper<ITradePost>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ITradePost);

  /// <summary>
  /// Stores an internal reference to the ITradePost object.
  /// </summary>
  internal override dynamic obj => Unbind(tradePost);

  //
  // ITradePost wrapper properties
  //

  /// <summary>
  /// The poster of the trade post.
  /// </summary>
  public User Poster => new(@base.Poster.Name);
  // public User? Poster => Optional<User>(@base.Poster?.Name);

  /// <summary>
  /// The format or type of the trade post.
  ///
  public TradePostFormat Format =>
    Try(() => Cast<TradePostFormat>(Unbind(this).Format),
        // Handle each case explicitly to avoid misreporting the post format.
        () => !(Wanted.Any() || Offered.Any())
          ? TradePostFormat.Message
          : TradePostFormat.OfferedWantedList,
        () => TradePostFormat.Invalid);

  /// <summary>
  /// The message of the trade post, if any.
  /// </summary>
  public string Message => @base.RawMessage;

  /// <summary>
  /// A list of cards that the poster wants to trade for.
  /// </summary>
  [Default(null)]
  public IEnumerable<CardQuantityPair> Wanted =>
    Map<CardQuantityPair>(@base.Wanted);

  /// <summary>
  /// A list of cards that the poster is offering to trade.
  /// </summary>
  [Default(null)]
  public IEnumerable<CardQuantityPair> Offered =>
    Map<CardQuantityPair>(@base.Offered);

  //
  // ITradePost wrapper events
  //

  public EventProxy FormatChanged =
    new(/* ITradePost */ tradePost, nameof(FormatChanged));

  public EventProxy MessageChanged =
    new(/* ITradePost */ tradePost, nameof(MessageChanged));
}
