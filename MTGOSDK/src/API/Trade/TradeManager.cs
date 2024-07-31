/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.API.Events;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

using WotC.MtGO.Client.Model.Trade;
using WotC.MtGO.Client.Model.Trade.Interfaces;


namespace MTGOSDK.API.Trade;

/// <summary>
/// A manager that provides access to trade posts, trade partners, and current
/// trades.
/// </summary>
/// <remarks>
/// This class provides a wrapper around the marketplace service, trade service,
/// and trade manager to provide a simplified interface for accessing information
/// about the client's ongoing trades.
/// </remarks>
public static class TradeManager
{
  /// <summary>
  /// The marketplace service that provides access to trade posts.
  /// </summary>
  private static readonly IMarketplace s_marketplace =
    ObjectProvider.Get<IMarketplace>();

  /// <summary>
  /// The trade service that provides access to trade partners and current trades.
  /// </summary>
  private static readonly ITrade s_tradeService =
    ObjectProvider.Get<ITrade>();

  /// <summary>
  /// The trade manager that provides access to trade events.
  /// </summary>
  private static readonly ITradeManager s_tradeManager =
    ObjectProvider.Get<ITradeManager>();

  //
  // IMarketPlace wrapper properties
  //

  /// <summary>
  /// A collection of all trade posts on the marketplace.
  /// </summary>
  public static IEnumerable<TradePost> AllPosts =>
    Map<TradePost>(s_marketplace.AllPosts);

  /// <summary>
  /// The trade post that the current user has created.
  /// </summary>
  /// <remarks>
  /// Returns null if the current user has not created a trade post or if the
  /// post has been deleted.
  /// </remarks>
  public static TradePost? MyPost =>
    Optional<TradePost>(s_marketplace.MyPost,
                        // If no post is found, the poster field will throw
                        // a null reference exception, so we need to handle
                        // this case explicitly for the null conditional.
                        post => Try<bool>(() => post.Poster != null));

  //
  // ITrade wrapper properties
  //

  /// <summary>
  /// A collection of all trade partners that the current user has traded with.
  /// </summary>
  public static IEnumerable<TradePartner> TradePartners =>
    Map<TradePartner>(s_tradeService.PreviousTradePartners);

  /// <summary>
  /// The current trade that the current user is engaged in.
  /// </summary>
  public static TradeEscrow? CurrentTrade =>
    Optional<TradeEscrow>(Unbind(s_tradeService.CurrentTradeEscrow));

  //
  // ITradeManager wrapper events
  //

  /// <summary>
  /// An event that is raised when a trade is started.
  /// </summary>
  public static EventProxy<TradeStartedEventArgs> TradeStarted =
    new(/* ITradeManager */ s_tradeManager, nameof(TradeStarted));

  /// <summary>
  /// An event that is raised when a trade is completed.
  /// </summary>
  public static EventProxy<TradeErrorEventArgs> TradeError =
    new(/* ITradeManager */ s_tradeManager, nameof(TradeError));
}
