/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting;

using WotC.MTGO.Common;
using WotC.MTGO.Common.Message;
using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Collection;
using MTGOSDK.API.Play;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.API.Events;

public sealed class Deck(dynamic deck) : CardGrouping
{
  private const uint ValidAnnotationMask = 0x1Fu;

  private static readonly DeckRegion[] SupportedRegions =
  [
    DeckRegion.MainDeck,
    DeckRegion.Sideboard,
    DeckRegion.CommandZone,
    DeckRegion.Planechase,
    DeckRegion.Vanguard,
    DeckRegion.Hidden,
  ];

  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IDeck);

  /// <summary>
  /// Stores an internal reference to the IDeck object.
  /// </summary>
  internal override dynamic obj => Bind<IDeck>(deck);

  /// <summary>
  /// Creates a new deck from the specified mainboard and sideboard.
  /// </summary>
  /// <param name="mainboard">The mainboard items to add to the deck.</param>
  /// <param name="sideboard">The sideboard items to add to the deck.</param>
  /// <returns>A new deck instance that can be added to the collection.</returns>
  public Deck(
    IEnumerable<CardQuantityPair> mainboard,
    IEnumerable<CardQuantityPair> sideboard)
      // We use a nested constructor to avoid using dynamic dispatch.
      // This gets unwrapped in the DLRWrapper constructor.
      : this(new Deck(RemoteClient.CreateInstance<WotC.MtGO.Client.Model.Core.Collection.Deck>()))
  {
    var deckItems = RemoteClient.CreateArray<DeckItem_t>(
      new[] { (mainboard, false), (sideboard, true) }
        .SelectMany(group => group.Item1,
          // Uses the standard deck permission code (215).
          (g, e) => new object[]
          {
            e.Id,
            GetDigitalObjectAnnotation(e.Annotation),
            215,
            e.Quantity,
            g.Item2,
          })
        .ToArray()
    );
    Unbind(this).ReconcileCards(deckItems);
  }

  /// <summary>
  /// Creates a new deck from the specified mainboard and sideboard.
  /// </summary>
  /// <param name="mainboard">The mainboard items to add to the deck.</param>
  /// <param name="sideboard">The sideboard items to add to the deck.</param>
  /// <param name="name">The name of the deck.</param>
  /// <param name="format">The format of the deck.</param>
  /// <returns>A new deck instance that can be added to the collection.</returns>
  public Deck(
    IEnumerable<CardQuantityPair> mainboard,
    IEnumerable<CardQuantityPair> sideboard,
    string name = null,
    PlayFormat format = null)
      : this(mainboard, sideboard)
  {
    Unbind(this).Name = name;
    Unbind(this).Format = format;
  }

  //
  // IDeck wrapper properties
  //

  public IEnumerable<DeckRegion> Regions =>
    Map<DeckRegion>(
      Unbind(this).Regions,
      Lambda<DeckRegion>(e => CastRemoteValue<DeckRegion>(e)));

  /// <summary>
  /// The unique identifier for this deck used for matchmaking.
  /// </summary>
  public int DeckId => @base.PreconstructedDeckId;

  /// <summary>
  /// Whether the deck is legal for play.
  /// </summary>
  public bool IsLegal => @base.IsLegal;

  //
  // IDeck wrapper methods
  //

  /// <summary>
  /// Proxy type for the client's DeckRegion class.
  /// </summary>
  private static readonly TypeProxy<dynamic> s_DeckRegion =
    new(typeof(WotC.MTGO.Common.DeckRegion));

  public dynamic GetRegionRef(DeckRegion region)
  {
    ValidateRegion(region);
    string key;
    switch (region)
    {
      case DeckRegion.CommandZone:
        key = "Command Zone";
        break;
      default:
        key = Enum.GetName(typeof(DeckRegion), region);
        break;
    }

    return RemoteClient.InvokeMethod(s_DeckRegion, "GetFromKey", null, key);
  }

  /// <summary>
  /// Returns the number of cards in the specified region.
  /// </summary>
  /// <param name="region">The deck region to count cards in.</param>
  public int GetRegionCount(DeckRegion region) =>
    Unbind(this).GetRegionCount(GetRegionRef(ValidateRegion(region)));

  /// <summary>
  /// Returns the cards in the specified region.
  /// </summary>
  /// <param name="region">The deck region to return cards from.</param>
  /// <returns>An iterator of CardQuantityPair objects.</returns>
  public IEnumerable<CardQuantityPair> GetCards(DeckRegion region) =>
    Map<CardQuantityPair>(
      Unbind(this).GetRegionCards(
          GetRegionRef(ValidateRegion(region))));

  /// <summary>
  /// Returns the total quantity of a card in the specified region.
  /// </summary>
  /// <param name="region">The deck region to count the card in.</param>
  /// <param name="catalogId">The catalog id of the card to count.</param>
  /// <returns>The total quantity of the card.</returns>
  public int GetQuantity(DeckRegion region, int catalogId) =>
    GetCards(region)
      .Where(pair => pair.Id == catalogId)
      .Sum(pair => pair.Quantity);

  public override IReadOnlyList<CardGroupingItemSnapshot> GetItemSnapshot() =>
    Regions
      .Distinct()
      .SelectMany(region => SnapshotItems(
        (object)Unbind(this).GetRegionCards(GetRegionRef(region)),
        region))
      .ToArray();

  /// <summary>
  /// Adds cards to a deck region.
  /// </summary>
  /// <remarks>
  /// This mirrors MTGO's native operation. Cards can be rejected for legality,
  /// truncated at the deck size limit, or redirected to a special region.
  /// The return value only indicates whether anything changed. Changes remain
  /// queued until <see cref="CollectionManager.SavePendingUpdates"/> is called.
  /// </remarks>
  public bool AddCards(
    DeckRegion region,
    IEnumerable<CardQuantityPair> cards,
    ulong? operationId = null,
    bool enforceLegality = true)
  {
    ValidateRegion(region);
    var items = NormalizeItems(cards);
    if (items.Count == 0)
      return false;

    EnsureMutable();
    return Unbind(this).AddCardsToRegion(
      GetRegionRef(region),
      CreateRemoteItems(items),
      operationId,
      enforceLegality);
  }

  /// <summary>
  /// Removes cards from a deck region.
  /// </summary>
  /// <returns>The quantities actually removed by MTGO.</returns>
  public IReadOnlyList<CardQuantityPair> RemoveCards(
    DeckRegion region,
    IEnumerable<CardQuantityPair> cards,
    ulong? operationId = null)
  {
    ValidateRegion(region);
    var items = NormalizeItems(cards);
    if (items.Count == 0)
      return Array.Empty<CardQuantityPair>();

    EnsureMutable();
    var removed = Unbind(this).RemoveCardsFromRegion(
      GetRegionRef(region),
      CreateRemoteItems(items),
      operationId);
    return Map<CardQuantityPair>((object)removed).ToArray();
  }

  /// <summary>
  /// Reconciles every deck region to the supplied desired state.
  /// </summary>
  /// <remarks>
  /// Regions omitted from <paramref name="desiredItems"/> are cleared. The
  /// returned snapshot is read after MTGO applies its legality and region rules.
  /// </remarks>
  public IReadOnlyList<CardGroupingItemSnapshot> ReplaceCards(
    IEnumerable<CardGroupingItemSnapshot> desiredItems,
    ulong? operationId = null,
    bool enforceLegality = true)
  {
    ArgumentNullException.ThrowIfNull(desiredItems);
    EnsureMutable();

    var desired = NormalizeSnapshots(desiredItems);
    if (MaxItems > 0 && desired.Sum(item => item.Quantity) > MaxItems)
      throw new ArgumentException(
        $"The desired deck contains more than the maximum of {MaxItems} cards.",
        nameof(desiredItems));

    var current = GetItemSnapshot();
    var currentByKey = current.ToDictionary(ItemKey);
    var desiredByKey = desired.ToDictionary(ItemKey);

    foreach (var item in current)
    {
      var key = ItemKey(item);
      var targetQuantity = desiredByKey.TryGetValue(key, out var target)
        ? target.Quantity
        : 0;
      if (item.Quantity > targetQuantity)
      {
        RemoveCards(
          item.Region,
          [new CardQuantityPair(item.CatalogId, item.Quantity - targetQuantity, item.Annotation)],
          operationId);
      }
    }

    foreach (var item in desired)
    {
      var key = ItemKey(item);
      var currentQuantity = currentByKey.TryGetValue(key, out var existing)
        ? existing.Quantity
        : 0;
      if (item.Quantity > currentQuantity)
      {
        AddCards(
          item.Region,
          [new CardQuantityPair(item.CatalogId, item.Quantity - currentQuantity, item.Annotation)],
          operationId,
          enforceLegality);
      }
    }

    return GetItemSnapshot();
  }

  internal void EnsureMutable()
  {
    if (@base.IsSystemDefined)
      throw new InvalidOperationException("System-defined decks cannot be modified.");
    if (@base.IsLocked)
      throw new InvalidOperationException("Locked decks cannot be modified.");
  }

  internal void ValidateContents() =>
    _ = NormalizeSnapshots(GetItemSnapshot());

  private static DeckRegion ValidateRegion(DeckRegion region)
  {
    if (!SupportedRegions.Contains(region))
      throw new ArgumentOutOfRangeException(nameof(region), region,
        "A single concrete deck region is required.");
    return region;
  }

  private static List<CardQuantityPair> NormalizeItems(
    IEnumerable<CardQuantityPair> cards)
  {
    ArgumentNullException.ThrowIfNull(cards);
    var normalized = new Dictionary<(int Id, uint Annotation), int>();
    foreach (var card in cards)
    {
      ArgumentNullException.ThrowIfNull(card);
      if (card.Id <= 0)
        throw new ArgumentOutOfRangeException(nameof(cards), "Card catalog IDs must be positive.");
      if (card.Quantity <= 0)
        throw new ArgumentOutOfRangeException(nameof(cards), "Card quantities must be positive.");
      ValidateAnnotation(card.Annotation);
      var key = (card.Id, card.Annotation);
      normalized[key] = checked(normalized.GetValueOrDefault(key) + card.Quantity);
    }

    return normalized
      .Select(item => new CardQuantityPair(item.Key.Id, item.Value, item.Key.Annotation))
      .ToList();
  }

  private static IReadOnlyList<CardGroupingItemSnapshot> NormalizeSnapshots(
    IEnumerable<CardGroupingItemSnapshot> items)
  {
    var normalized = new Dictionary<(int Id, DeckRegion Region, uint Annotation), int>();
    foreach (var item in items)
    {
      ValidateRegion(item.Region);
      if (item.CatalogId <= 0)
        throw new ArgumentOutOfRangeException(nameof(items), "Card catalog IDs must be positive.");
      if (item.Quantity <= 0)
        throw new ArgumentOutOfRangeException(nameof(items), "Card quantities must be positive.");
      ValidateAnnotation(item.Annotation);
      var key = (item.CatalogId, item.Region, item.Annotation);
      normalized[key] = checked(normalized.GetValueOrDefault(key) + item.Quantity);
    }

    return normalized
      .Select(item => new CardGroupingItemSnapshot(
        item.Key.Id,
        item.Key.Region,
        item.Key.Annotation,
        item.Value))
      .ToArray();
  }

  private static void ValidateAnnotation(uint annotation)
  {
    if ((annotation & ~ValidAnnotationMask) != 0)
      throw new ArgumentOutOfRangeException(nameof(annotation), annotation,
        "The annotation contains unsupported bits.");
  }

  private static uint GetDigitalObjectAnnotation(uint annotation)
  {
    var value = (AttributeAnnotation)annotation;
    var result = DigitalObjectAnnotationEnum.NotSet;
    if (value.HasFlag(AttributeAnnotation.DeckbuildingColorWhite))
      result |= DigitalObjectAnnotationEnum.V1DeckbuildingColorWhite;
    if (value.HasFlag(AttributeAnnotation.DeckbuildingColorBlue))
      result |= DigitalObjectAnnotationEnum.V1DeckbuildingColorBlue;
    if (value.HasFlag(AttributeAnnotation.DeckbuildingColorBlack))
      result |= DigitalObjectAnnotationEnum.V1DeckbuildingColorBlack;
    if (value.HasFlag(AttributeAnnotation.DeckbuildingColorRed))
      result |= DigitalObjectAnnotationEnum.V1DeckbuildingColorRed;
    if (value.HasFlag(AttributeAnnotation.DeckbuildingColorGreen))
      result |= DigitalObjectAnnotationEnum.V1DeckbuildingColorGreen;
    return (uint)result;
  }

  private static (int Id, DeckRegion Region, uint Annotation) ItemKey(
    CardGroupingItemSnapshot item) =>
    (item.CatalogId, item.Region, item.Annotation);

  private static dynamic CreateRemoteItems(IEnumerable<CardQuantityPair> items) =>
    RemoteClient.CreateArray<WotC.MtGO.Client.Model.Core.CardQuantityPair>(
      items.Select(item => new object[]
      {
        item.Id,
        item.Quantity,
        215,
        (WotC.MtGO.Client.Model.Collection.AttributeAnnotation)item.Annotation,
      }).ToArray());

  // public override bool ItemIsLegalInThis(ICardDefinition card)
  // public static bool ItemIsLegalForDeck(ICardDefinition card, IPlayFormat playFormat = null)

  // public void ValidateCompanion()
  // {
  //   m_deck.ValidateCompanion();
  //   return m_deck.CompanionValidatorResults; // Public but not in IDeck
  // }

  //
  // ICardGrouping wrapper events
  //

  public EventProxy<CardGroupingItemsChangedEventArgs> ItemsAddedOrRemoved =
    new(/* ICardGrouping */ deck, nameof(ItemsAddedOrRemoved));
}
