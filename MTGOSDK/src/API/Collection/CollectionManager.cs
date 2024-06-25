/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Concurrent;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MtGO.Client.Model.Collection;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

public static class CollectionManager
{
  //
  // CardDataManager wrapper methods
  //

  /// <summary>
  /// Manages the client's set information and card definitions.
  /// </summary>
  private static readonly ICardDataManager s_cardDataManager =
    ObjectProvider.Get<ICardDataManager>();

  //
  // ICardDefinition wrapper methods
  //

  /// <summary>
  /// A dictionary of cached Card objects.
  /// </summary>
  public static ConcurrentDictionary<int, Card> Cards { get; } = new();

  /// <summary>
  /// Returns a list of catalog ids for the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of catalog ids.</returns>
  public static IList<int> GetCardIds(string cardName) =>
    Map<IList, int>(
      s_cardDataManager.GetCatalogIdsForNameInPreferentialOrder(cardName, true));

  /// <summary>
  /// Returns a card object by the given catalog id.
  /// </summary>
  /// <param name="id">The catalog id of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given catalog id.
  /// </exception>
  public static Card GetCard(int id)
  {
    if (!Cards.TryGetValue(id, out var card))
    {
      Cards[id] = card = new Card(
        s_cardDataManager.GetCardDefinitionForCatId(id)
        ?? throw new KeyNotFoundException(
            $"No card found with catalog id #{id}.")
      );
      // Set callback to remove user from cache when the client is disposed.
      RemoteClient.Disposed += (s, e) => Cards.TryRemove(id, out _);
    }

    return card;
  }

  /// <summary>
  /// Returns a card object by the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given name.
  /// </exception>
  public static Card GetCard(string cardName) =>
    GetCard(
      (int?)GetCardIds(cardName).FirstOrDefault()
        ?? throw new KeyNotFoundException(
            $"No card found with name \"{cardName}\".")
    );

  /// <summary>
  /// Returns a list of card objects by the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of card objects.</returns>
  public static IEnumerable<Card> GetCards(string cardName) =>
    Map<IEnumerable, int, Card>(GetCardIds(cardName), GetCard);

  //
  // ICardSet wrapper methods
  //

  /// <summary>
  /// A dictionary of all card sets by their set code.
  /// </summary>
  private static dynamic AllCardSetsByCode =>
    // TODO: Fix type casting of nested types, i.e. Dictionary<string, CardSet>.
    Unbind(s_cardDataManager).AllCardSetsByCode;

  /// <summary>
  /// Returns a set object by the given set code.
  /// </summary>
  /// <param name="setCode">The set code of the set to return.</param>
  /// <returns>A new set object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no set is found with the given set code.
  /// </exception>
  public static Set GetSet(string setCode)
  {
    if (!AllCardSetsByCode.ContainsKey(setCode))
      throw new KeyNotFoundException($"No set found with code \"{setCode}\".");

    return new(AllCardSetsByCode[setCode]);
  }

  //
  // CollectionGroupingManager wrapper methods
  //

  /// <summary>
  /// Manages the client's binders, wish lists, and decks.
  /// </summary>
  private static readonly ICollectionGroupingManager s_collectionGroupingManager =
    ObjectProvider.Get<ICollectionGroupingManager>();

  private static ICardGrouping GetCollectionItem(int id) =>
    s_collectionGroupingManager.GetCardGroupingById(id);

  //
  // ICollectionGrouping wrapper methods
  //

  /// <summary>
  /// Fetches the collection instance from the collection grouping manager.
  /// </summary>
  /// <returns>The internal collection instance.</returns>
  internal static ICollectionGrouping GetCollection() =>
   s_collectionGroupingManager.Collection;

  /// <summary>
  /// The main collection of all cards and items in the user's collection.
  /// </summary>
  public static Collection Collection => new();

  // public void ExportCollectionItem(CollectionItem item, string fileName) =>
  //   s_collectionGroupingManager.ExportCollectionItem(item.obj, fileName);

  //
  // IBinder wrapper methods
  //

  public static IEnumerable<Binder> Binders =>
    Map<Binder>(Unbind(s_collectionGroupingManager).BinderFolder.Contents);

  /// <summary>
  /// The last used binder for trading.
  /// </summary>
  public static Binder? LastUsedBinder =>
    Retry(() => new Binder(s_collectionGroupingManager.LastUsedBinder));

  /// <summary>
  /// The user's wish list.
  /// </summary>
  public static Binder WishList =>
    new(s_collectionGroupingManager.WishList);

  /// <summary>
  /// Returns a binder object by the given binder id.
  /// </summary>
  /// <param name="id">The id of the binder to return.</param>
  /// <returns>A new binder object.</returns>
  public static Binder GetBinder(int id) => new(Unbind(GetCollectionItem(id)));

  // IBinder ImportTextDeckAsBinder(FileInfo textFileToImport, string name, IVisualResource binderImage);
  // IBinder CreateNewBinder(string name, IVisualResource binderImage = null, IEnumerable<ICardDefinition> initialCards = null);

  //
  // IDeck wrapper methods
  //

  /// <summary>
  /// Returns all decks in the user's collection.
  /// </summary>
  public static IEnumerable<Deck> Decks =>
    s_collectionGroupingManager.Folders
      .SelectMany(folder =>
        folder.Contents
          .Where(grouping => grouping is ICardGrouping)
          .Select(grouping => new Deck(Unbind(grouping)))
      );

  /// <summary>
  /// Returns a deck object by the given deck id.
  /// </summary>
  /// <param name="id">The id of the deck to return.</param>
  /// <returns>A new deck object.</returns>
  public static Deck GetDeck(int id) => new(Unbind(GetCollectionItem(id)));

  // IDeck ImportTextDeck(FileInfo textFileToImport, string name, IPlayFormat format, IVisualResource deckBoxImage, IDeckFolder location);
  // IDeck CreateNewDeck(string name, IPlayFormat format, IVisualResource deckBoxImage = null, IDeckFolder location = null, IEnumerable<ICardDefinition> initialCards = null);

  //
  // ICollectionGroupingManager wrapper events
  //

  public static EventProxy LastUsedBinderChanged =
    new(s_collectionGroupingManager, nameof(LastUsedBinderChanged));

  public static EventProxy DeckCreatedOrImported =
    new(s_collectionGroupingManager, nameof(DeckCreatedOrImported));

  public static EventProxy DeckFolderDeleted =
    new(s_collectionGroupingManager, nameof(DeckFolderDeleted));

  public static EventProxy DeckDeleted =
    new(s_collectionGroupingManager, nameof(DeckDeleted));
}
