/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Types;

using static MTGOSDK.Core.Reflection.DLRWrapper;

using WotC.MtGO.Client.Model.Trade;
using WotC.MtGO.Client.Model.Trade.Interfaces;

namespace MTGOSDK.API.Trade;
using MTGOSDK.API.Trade.Enums;

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
  // ITradeManager wrapper properties
  //

  /// <summary>
  /// Whether the current user is currently engaged in a trade.
  /// </summary>
  public static bool IsCurrentlyInTrade =>
    Unbind(s_tradeManager).IsUserCurrentlyInATrade();

  /// <summary>
  /// A collection of all open trades that the current user is engaged in.
  /// </summary>
  public static IEnumerable<TradeEscrow> OpenTrades =>
    Map<TradeEscrow>(Unbind(s_tradeManager).m_openTrades);

  //
  // Batch serialization methods
  //

  /// <summary>
  /// Serializes marketplace trade posts using cross-post batch fetching.
  /// </summary>
  /// <typeparam name="TInterface">The interface type to serialize trade posts as.</typeparam>
  /// <param name="maxItems">Maximum number of items to serialize (0 = no limit).</param>
  /// <param name="format">Optional trade post format to filter on before serialization.</param>
  /// <param name="posterNameSearch">Optional poster name substring to filter on before serialization.</param>
  /// <param name="messageSearch">Optional message substring to filter on before serialization.</param>
  /// <returns>Enumerable of serialized posts implementing TInterface.</returns>
  /// <remarks>
  /// This method uses a single IPC call to fetch all posts' primitive
  /// properties, avoiding live per-item enumeration of the marketplace
  /// collection.
  /// </remarks>
  public static IEnumerable<TInterface> SerializePostsAs<TInterface>(
    int maxItems = 0,
    TradePostFormat? format = null,
    string? posterNameSearch = null,
    string? messageSearch = null)
    where TInterface : class
  {
    DynamicRemoteObject posts = SortPostsByPosterName(
      GetFilteredPosts(
        format,
        posterNameSearch,
        messageSearch));

    return SerializeDroAs<TInterface, TradePost>(
      posts,
      "",
      maxItems);
  }

  public static int CountPosts(
    TradePostFormat? format = null,
    string? posterNameSearch = null,
    string? messageSearch = null)
  {
    DynamicRemoteObject posts = GetFilteredPosts(
      format,
      posterNameSearch,
      messageSearch);

    return RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CollectionHelpers",
      "Count",
      args: new object[] { posts }
    );
  }

  private static DynamicRemoteObject GetFilteredPosts(
    TradePostFormat? format,
    string? posterNameSearch,
    string? messageSearch)
  {
    DynamicRemoteObject allPosts = (DynamicRemoteObject)Unbind(s_marketplace).AllPosts;
    DynamicRemoteObject posts = format switch
    {
      TradePostFormat.Message =>
        FilterPostsByFormat(allPosts, TradePostFormat.Message),
      TradePostFormat.OfferedWantedList =>
        FilterPostsByFormat(allPosts, TradePostFormat.OfferedWantedList),
      _ => allPosts
    };
    if (!string.IsNullOrWhiteSpace(posterNameSearch))
      posts = FilterPostsByPosterName(posts, posterNameSearch.Trim());
    if (!string.IsNullOrWhiteSpace(messageSearch))
      posts = FilterPostsByMessage(posts, messageSearch.Trim());

    return posts;
  }

  private static DynamicRemoteObject FilterPostsByFormat(
    DynamicRemoteObject posts,
    TradePostFormat format)
  {
    return RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CollectionHelpers",
      "WherePropertyEnumName",
      args: new object[] { posts, "Format", format.ToString() }
    );
  }

  private static DynamicRemoteObject FilterPostsByPosterName(
    DynamicRemoteObject posts,
    string posterNameSearch)
  {
    return RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CollectionHelpers",
      "WherePropertyStringContains",
      args: new object[] { posts, "Poster.Name", posterNameSearch, true }
    );
  }

  private static DynamicRemoteObject FilterPostsByMessage(
    DynamicRemoteObject posts,
    string messageSearch)
  {
    return RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CollectionHelpers",
      "WherePropertyStringContains",
      args: new object[] { posts, "RawMessage", messageSearch, true }
    );
  }

  private static DynamicRemoteObject SortPostsByPosterName(
    DynamicRemoteObject posts)
  {
    return RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CollectionHelpers",
      "OrderByProperty",
      args: new object[] { posts, "Poster.Name", false }
    );
  }

  /// <summary>
  /// Async variant of <see cref="SerializePostsAs{TInterface}"/> that does not block
  /// a thread pool thread while waiting for the batch IPC response.
  /// </summary>
  public static Task<IList<TInterface>> SerializePostsAsAsync<TInterface>(
    int maxItems = 0,
    TradePostFormat? format = null,
    string? posterNameSearch = null,
    string? messageSearch = null)
    where TInterface : class
    => Task.FromResult<IList<TInterface>>(
      SerializePostsAs<TInterface>(
        maxItems,
        format,
        posterNameSearch,
        messageSearch).ToList());

  //
  // IMarketPlace wrapper events
  //

  public static EventHookProxy<DateTime> MarketplaceUpdated =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Trade.Marketplace>(),
      "OnMiniUserMarketplaceList",
      new((instance, args) =>
      {
        var msg = args[0];
        if (Try<bool>(() => msg.MarketPlaceUsers == null)) return null;

        return instance.__timestamp;
      }),
      HarmonyPatchPosition.Postfix
    );

  //
  // ITradeManager wrapper events
  //

  /// <summary>
  /// Event triggered when any trade escrow is opened.
  /// </summary>
  /// <remarks>
  /// The boolean event value is true for player trades and false for
  /// non-player escrows such as opening a pack.
  /// </remarks>
  public static EventHookProxy<TradeEscrow, bool> TradeStarted =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Trade.TradeManager>(),
      "OnTradeStarted",
      new((instance, args) =>
      {
        var trade = new TradeEscrow(args[0]);
        var isPlayerTrade = (bool)args[1];

        // Set event timestamp for correlation.
        var timestamp = instance.__timestamp;
        Unbind(trade).__timestamp = timestamp;

        return (trade, isPlayerTrade);
      }),
      HarmonyPatchPosition.Postfix
    );

  /// <summary>
  /// Event triggered when any trade escrow changes state or refreshes.
  /// </summary>
  public static EventHookProxy<
    TradeEscrow,
    (TradeState OldState, TradeState NewState)> TradeStateChanged =
      new(
        new TypeProxy<WotC.MtGO.Client.Model.Trade.TradeManager>(),
        "OpenTradeTradeStateChanged",
        new((instance, args) =>
        {
          dynamic e = args[1];
          TradeEscrow trade = new(e.Trade);
          var states = (
            OldState: Cast<TradeState>(e.OldState),
            NewState: Cast<TradeState>(e.NewState));

          // Set event timestamp for correlation.
          var timestamp = instance.__timestamp;
          Unbind(trade).__timestamp = timestamp;

          return (trade, states);
        }),
        HarmonyPatchPosition.Postfix
      );

  /// <summary>
  /// Event triggered whenever MTGO creates a trade error.
  /// </summary>
  /// <remarks>
  /// The trade is null when the error occurs before an escrow is created.
  /// Specialized MTGO error details are intentionally flattened to
  /// <see cref="Trade.Enums.TradeError"/>.
  /// </remarks>
  public static EventHookProxy<TradeEscrow?, TradeError> TradeError =
    new(
      //
      // Hooks into the TradeErrorEventArgs constructor
      //
      // Note that the ClientTradeErrorEnum corresponds to MTGO's copy
      // of our TradeError enum we vendor in MTGOSDK.API.Trade.Enums.
      //
      typeof(WotC.MtGO.Client.Model.Trade.Events.TradeErrorEventArgs),
      [
        typeof(WotC.MtGO.Client.Model.Trade.Enums.ClientTradeErrorEnum),
        typeof(ITradeEscrow)
      ],
      new((instance, args) =>
      {
        TradeEscrow? trade = Try(() =>
          args[1] == null ? null : new TradeEscrow(args[1]));
        TradeError error = Cast<TradeError>(args[0]);

        // Set event timestamp for correlation.
        var timestamp = instance.__timestamp;
        Unbind(trade).__timestamp = timestamp;

        return (trade, error);
      })
    );
}
