/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Reflection;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Collection;
using WotC.MtGO.Client.Model.Core;


namespace MTGOSDK.API.Collection;

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
  /// Returns a list of catalog ids for the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of catalog ids.</returns>
  public static IList<int> GetCardIds(string cardName) =>
    s_cardDataManager.GetCatalogIdsForNameInPreferentialOrder(cardName, true);

  /// <summary>
  /// Returns a card object by the given catalog id.
  /// </summary>
  /// <param name="id">The catalog id of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given catalog id.
  /// </exception>
  public static Card GetCard(int id) =>
    new(
      s_cardDataManager.GetCardDefinitionForCatId(id)
        ?? throw new KeyNotFoundException(
            $"No card found with catalog id #{id}.")
    );

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
  public static IEnumerable<Card> GetCards(string cardName)
  {
    foreach (var id in GetCardIds(cardName))
      yield return GetCard(id);
  }

  //
  // ICardSet wrapper methods
  //

  /// <summary>
  /// A dictionary of all card sets by their set code.
  /// </summary>
  private static dynamic AllCardSetsByCode =>
    // TODO: Fix type casting of nested types, i.e. Dictionary<string, CardSet>.
    Proxy<dynamic>.From(s_cardDataManager).AllCardSetsByCode;

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

  // public void ExportCollectionItem(CollectionItem item, string fileName) =>
  //   s_collectionGroupingManager.ExportCollectionItem(item.obj, fileName);

  //
  // IBinder wrapper methods
  //

  /// <summary>
  /// The last used binder for trading.
  /// </summary>
  public static Binder LastUsedBinder =>
    new(s_collectionGroupingManager.LastUsedBinder);

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
  public static Binder GetBinder(int id) => new(GetCollectionItem(id));

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
      .SelectMany(/* IDeckFolder */ folder =>
        folder.Contents
          .Where(/* ICardGrouping */ grouping =>
            {
              // This is a simple test to check whether the grouping is a deck.
              try   { return grouping is ICardGrouping; }
              // If the grouping has an invalid address or isn't a deck, ignore.
              catch { return false; }
            })
          .Select(deck => new Deck(deck))
      );

  /// <summary>
  /// Returns a deck object by the given deck id.
  /// </summary>
  /// <param name="id">The id of the deck to return.</param>
  /// <returns>A new deck object.</returns>
  public static Deck GetDeck(int id) => new(GetCollectionItem(id));

  // IDeck ImportTextDeck(FileInfo textFileToImport, string name, IPlayFormat format, IVisualResource deckBoxImage, IDeckFolder location);
  // IDeck CreateNewDeck(string name, IPlayFormat format, IVisualResource deckBoxImage = null, IDeckFolder location = null, IEnumerable<ICardDefinition> initialCards = null);
}
