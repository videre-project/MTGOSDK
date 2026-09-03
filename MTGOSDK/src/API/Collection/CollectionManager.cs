/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Collection;
using WotC.MtGO.Client.Model.Core;
using WotC.MtGO.Client.Model.Core.FileImport;

using MTGOSDK.API.Trade;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Types;
using static MTGOSDK.Core.Reflection.DLRWrapper;


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

  static CollectionManager()
  {
    ObjectCache.OnReset += delegate
    {
      s_cardIdToDefinitions = null;
      s_cardNameToDefinitions = null;
    };
  }

  //
  // ICardDefinition wrapper methods
  //

  private static dynamic s_cardIdToDefinitions
  {
    get => field ??= Unbind(s_cardDataManager).DigitalObjectsByCatId;
    set => field = value;
  }

  private static dynamic s_cardNameToDefinitions
  {
    get => field ??= Unbind(s_cardDataManager).NameToCardDefinitions;
    set => field = value;
  }

  /// <summary>
  /// Returns a list of catalog ids for the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of catalog ids.</returns>
  public static IList<int> GetCardIds(string cardName) =>
    Map<IList, int>(
      Try(() => s_cardNameToDefinitions[cardName.ToLower()])
        ?? throw new KeyNotFoundException(
          $"No card found with name \"{cardName}\"."),
      Lambda<int>(c => c.Id)
    );

  /// <summary>
  /// Returns a list of card objects for the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of card objects.</returns>
  public static IList<Card> GetCardPrintings(string cardName) =>
    Map<IList, Card>(
      Try(() => s_cardNameToDefinitions[cardName.ToLower()])
        ?? throw new KeyNotFoundException(
          $"No card found with name \"{cardName}\"."),
      proxy: true
    );

  /// <summary>
  /// Returns a card object by the given catalog id.
  /// </summary>
  /// <param name="id">The catalog id of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given catalog id.
  /// </exception>
  public static Card GetCard(int id) =>
    new(Try(() => s_cardIdToDefinitions[id])
      ?? throw new KeyNotFoundException($"No card found with catalog id #{id}."));

  /// <summary>
  /// Returns a card object by the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to return.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given name.
  /// </exception>
  public static Card GetCard(string cardName) => GetCardPrintings(cardName)[0];

  /// <summary>
  /// Returns a list of card objects by the given card name.
  /// </summary>
  /// <param name="cardName">The name of the card to query.</param>
  /// <returns>A list of card objects.</returns>
  public static IEnumerable<Card> GetCards(string cardName) =>
    Map<IEnumerable, int, Card>(GetCardIds(cardName), GetCard);

  /// <summary>
  /// Returns a card object by the given card texture number (CTN).
  /// </summary>
  /// <param name="textureId">The card texture number to look up.</param>
  /// <returns>A new card object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no card is found with the given texture number.
  /// </exception>
  public static Card GetCardByTextureId(int textureId) =>
    new(
      Unbind(s_cardDataManager).GetCardDefinitionForTextureNumber(textureId, true)
        ?? throw new KeyNotFoundException(
          $"No card found with texture number #{textureId}."));

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

  /// <summary>
  /// Whether MTGO has loaded the online collection and can accept mutations.
  /// </summary>
  public static bool IsLoadedOnline => s_collectionGroupingManager.IsLoadedOnline;

  /// <summary>
  /// The maximum name length accepted by MTGO for a deck.
  /// </summary>
  public static int MaxDeckNameLength => s_collectionGroupingManager.MaxDeckNameLength;

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
    Map<Binder>(
      ((DynamicRemoteObject)
        Unbind(s_collectionGroupingManager).BinderFolder.Contents)
          .WhereAssignableTo<IBinder>());

  /// <summary>
  /// The last used binder for trading.
  /// </summary>
  public static Binder? LastUsedBinder =>
    Optional<Binder>(s_collectionGroupingManager.LastUsedBinder);

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
    Map<Deck>(
      ((DynamicRemoteObject)
        Unbind(s_collectionGroupingManager).m_groupingsById.Values)
          .WhereAssignableTo<IDeck>()
    );

  /// <summary>
  /// Returns a deck object by the given deck id.
  /// </summary>
  /// <param name="id">The id of the deck to return.</param>
  /// <returns>A new deck object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if no deck is found with the given id.
  /// </exception>
  public static Deck GetDeck(int id) =>
    new(
      Try(() => Unbind(s_collectionGroupingManager).m_groupingsById[id])
        ?? throw new KeyNotFoundException($"No deck found with id #{id}."));

  /// <summary>
  /// Returns a deck object by the given deck name.
  /// </summary>
  /// <param name="name">The name of the deck to return.</param>
  /// <returns>A new deck object.</returns>
  public static Deck GetDeck(string name) =>
    new(
      ((DynamicRemoteObject)
       Unbind(s_collectionGroupingManager).m_groupingsById.Values)
        .WhereAssignableTo<IDeck>()
        .Filter<IDeck>(e => e.Name == name)[0]
    );

  /// <summary>
  /// Returns a non-conflicting deck name using MTGO's naming rules.
  /// </summary>
  public static string GetUniqueDeckName(string name)
  {
    var normalized = NormalizeDeckName(name);
    return s_collectionGroupingManager.FindNonDuplicateUnrestrictedDeckName(normalized);
  }

  /// <summary>
  /// Flushes all queued grouping changes to MTGO's online collection service.
  /// </summary>
  /// <remarks>
  /// This sends every pending grouping and returns after sending.
  /// Note that MTGO does not provide a server acknowledgement through this API.
  /// </remarks>
  public static void SavePendingUpdates() =>
    s_collectionGroupingManager.SavePendingUpdates();

  /// <summary>
  /// Permanently deletes a registered deck from the user's MTGO collection.
  /// </summary>
  /// <remarks>
  /// MTGO removes the deck from the local collection immediately and sends the
  /// delete request asynchronously. This operation cannot be undone through the
  /// SDK. The deck's server id and name must still identify the registered deck,
  /// which prevents a stale wrapper from deleting a different deck by id alone.
  /// </remarks>
  public static void DeleteDeck(Deck deck)
  {
    ArgumentNullException.ThrowIfNull(deck);
    if (!IsLoadedOnline)
      throw new InvalidOperationException("The MTGO collection is not loaded online.");
    if (deck.NetDeckId <= 0)
      throw new ArgumentException("Only a registered deck can be deleted.", nameof(deck));

    var registered = GetDeck(deck.NetDeckId);
    if (!string.Equals(registered.Name, deck.Name, StringComparison.Ordinal))
      throw new InvalidOperationException(
        "The deck id is now registered to a deck with a different name.");

    registered.EnsureMutable();
    s_collectionGroupingManager.Delete(Bind<IDeck>(registered));
  }

  /// <summary>
  /// Imports an in-memory deck into the user's MTGO collection.
  /// </summary>
  /// <remarks>
  /// Add mode registers a new deck immediately and may return with NetDeckId set
  /// to zero while MTGO assigns the server ID asynchronously. Replace mode
  /// updates a matching deck in place and queues that update for an explicit
  /// <see cref="SavePendingUpdates"/> call.
  /// </remarks>
  public static Deck ImportDeck(
    Deck deck,
    DeckImportMode mode = DeckImportMode.Add)
  {
    ArgumentNullException.ThrowIfNull(deck);
    if (!Enum.IsDefined(mode))
      throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown deck import mode.");
    if (!IsLoadedOnline)
      throw new InvalidOperationException("The MTGO collection is not loaded online.");
    if (deck.NetDeckId > 0)
      throw new ArgumentException("Only an unregistered deck can be imported.", nameof(deck));

    var requestedName = NormalizeDeckName(deck.Name);
    deck.ValidateContents();
    if (mode == DeckImportMode.Replace)
    {
      var matches = Decks
        .Where(candidate => string.Equals(
          candidate.Name, requestedName, StringComparison.InvariantCultureIgnoreCase))
        .ToArray();
      if (matches.Length > 1)
        throw new InvalidOperationException(
          $"More than one deck matches the name '{requestedName}'.");
      if (matches.Length == 1)
      {
        var existing = matches[0];
        existing.EnsureMutable();
        if (deck.Format is { } format)
          Unbind(existing).Format = Unbind(format);
        existing.ReplaceCards(deck.GetItemSnapshot());
        return existing;
      }
    }

    var importName = mode == DeckImportMode.Add
      ? GetUniqueDeckName(requestedName)
      : requestedName;
    var imported = s_collectionGroupingManager.FinalizeImportedDeck(
      Bind<IDeck>(deck),
      FileImportFormatType.Dek,
      importName,
      deck.Format is { } importFormat ? Bind<IPlayFormat>(importFormat) : null,
      null,
      null);
    return new Deck(imported);
  }

  private static string NormalizeDeckName(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    var normalized = name.Normalize();
    if (normalized.Length > MaxDeckNameLength)
      throw new ArgumentException(
        $"Deck names cannot exceed {MaxDeckNameLength} characters.", nameof(name));
    return normalized;
  }

  public static readonly Func<dynamic, CardGrouping> CardGroupingFactory =
    new(FromCardGrouping);

  private static CardGrouping FromCardGrouping(dynamic cardGrouping)
  {
    CardGroupingType groupingType = CastRemoteValue<CardGroupingType>(
      cardGrouping.GroupingType);
    var groupingObject = new CardGrouping(cardGrouping);

    // Map each event type to its corresponding wrapper class.
    switch (groupingType)
    {
      case CardGroupingType.Deck:
        groupingObject = new Deck(groupingObject);
        break;
      case CardGroupingType.Binder:
      case CardGroupingType.Wishlist:
      case CardGroupingType.MegaBinder:
        groupingObject = new Binder(groupingObject);
        break;
      default:
        throw new InvalidOperationException($"Unknown grouping type: {groupingType}");
    }
#if DEBUG
    Log.Trace("Created new {Type} object for '{GroupingObject}'.",
        groupingObject.GetType().Name, groupingObject);
#endif
    return groupingObject;
  }

  //
  // ICollectionGroupingManager wrapper events
  //

  /// <summary>
  /// Event triggered when the items in the user's collection change.
  /// </summary>
  public static EventHookProxy<
    Collection,
    (
      IList<CardQuantityPair> Changes,
      TransactionCorrelation Correlation)> CollectionItemsChanged =
        new(
          new TypeProxy<WotC.MtGO.Client.Model.Core.Collection.CollectionGroupingManager>(),
          "Collection_ItemsAddedOrRemoved",
          new((instance, args) =>
          {
            var collection = Collection;
            var changeSet = new List<CardQuantityPair>();
            var e = args[1]; // CardGroupingItemsChangedEventArgs
            if (e.ItemsAdded != null)
            {
              changeSet.AddRange(Map<CardQuantityPair>(e.ItemsAdded));
            }
            if (e.ItemsRemoved != null)
            {
              changeSet.AddRange(Map(
                e.ItemsRemoved,
                Lambda<CardQuantityPair>(item =>
                  new(item, -item.Quantity))));
            }
            if (e.ItemsModified != null)
            {
              changeSet.AddRange(Map<CardQuantityPair>(e.ItemsModified));
            }

            IList<CardQuantityPair> changes = changeSet
              .Select(item => new CardQuantityPair(item.Id, item.Quantity))
              .ToArray();

            var correlation = new TransactionCorrelation(
              (DateTime)instance.__timestamp,
              (ulong?)e.OperationId,
              (Guid?)e.EscrowToken
            );

            return (collection, (changes, correlation));
          }),
          HarmonyPatchPosition.Postfix
        );

  /// <summary>
  /// Event triggered when the items in a card grouping (deck or binder) change.
  /// </summary>
  public static EventHookProxy<CardGrouping, IList<CardQuantityPair>> CardGroupingItemsChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Core.Collection.CollectionGroupingManager>(),
      "CardGrouping_ItemsAddedOrRemoved",
      new((instance, args) =>
      {
        var grouping = FromCardGrouping(args[0]); // Deck or Binder object

        var changeSet = new List<CardQuantityPair>();
        var e = args[1]; // CardGroupingItemsChangedEventArgs
        if (e.ItemsAdded != null)
        {
          changeSet.AddRange(Map<CardQuantityPair>(e.ItemsAdded));
        }
        if (e.ItemsRemoved != null)
        {
          changeSet.AddRange(Map(e.ItemsRemoved, Lambda<CardQuantityPair>(c => new(c, -c.Quantity))));
        }
        if (e.ItemsModified != null)
        {
          changeSet.AddRange(Map<CardQuantityPair>(e.ItemsModified));
        }

        return (grouping, changeSet);
      }),
      HarmonyPatchPosition.Postfix
    );

  /// <summary>
  /// Event triggered when a property of a card grouping (deck or binder) changes.
  /// </summary>
  public static EventHookProxy<CardGrouping, string> CardGroupingPropertyChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Core.Collection.CollectionGroupingManager>(),
      "CardGrouping_PropertyChanged",
      new((_, args) =>
      {
        var grouping = FromCardGrouping(args[0]); // Deck or Binder object
        var propertyName = args[1].PropertyName;
        switch (propertyName)
        {
          case "Format":
          case "Image":
          case "Name":
          case "NetDeckId":
            return (grouping, propertyName);
          default:
            return null; // Ignore other property changes
        }
      }),
      HarmonyPatchPosition.Postfix
    );

  public static EventHookProxy<CardGrouping, DateTime> LastUsedBinderChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Core.Collection.OnlineCollectionDataManager>(),
      "SetLastUsedBinderAndUpdateServer",
      new((instance, args) =>
      {
        int netDeckId = args[0];
        var grouping = FromCardGrouping(instance.m_groupingsFromCacheById[netDeckId]);

        return (grouping, instance.__timestamp);
      }),
      HarmonyPatchPosition.Postfix
    );

  public static EventHookProxy<CardGrouping, DateTime> DeckCreatedOrImported =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Core.Collection.CollectionGroupingManager>(),
      "RegisterGrouping",
      new((instance, args) =>
      {
        var grouping = FromCardGrouping(args[0]);

        return (grouping, instance.__timestamp);
      }),
      HarmonyPatchPosition.Postfix
    );

  public static EventHookProxy<CardGrouping, DateTime> DeleteGrouping =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Core.Collection.CollectionGroupingManager>(),
      "DeleteGrouping",
      new((instance, args) =>
      {
        var grouping = FromCardGrouping(args[0]);

        return (grouping, instance.__timestamp);
      }),
      HarmonyPatchPosition.Postfix
    );
}
