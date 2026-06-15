/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.Core.Reflection.DLRWrapper;

using MTGOSDK.API.Trade.Enums;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model.Trade;
using WotC.MtGO.Client.Model.Trade.Interfaces;

using ApiTradePostFormat = MTGOSDK.API.Trade.Enums.TradePostFormat;


namespace MTGOSDK.API.Trade;
using static MTGOSDK.API.Events;

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
    ApiTradePostFormat? format = null,
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
    ApiTradePostFormat? format = null,
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
    ApiTradePostFormat? format,
    string? posterNameSearch,
    string? messageSearch)
  {
    DynamicRemoteObject allPosts = (DynamicRemoteObject)Unbind(s_marketplace).AllPosts;
    DynamicRemoteObject posts = format switch
    {
      ApiTradePostFormat.Message =>
        FilterPostsByFormat(allPosts, ApiTradePostFormat.Message),
      ApiTradePostFormat.OfferedWantedList =>
        FilterPostsByFormat(allPosts, ApiTradePostFormat.OfferedWantedList),
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
    ApiTradePostFormat format)
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
    ApiTradePostFormat? format = null,
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
