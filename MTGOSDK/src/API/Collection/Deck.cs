/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting;

using WotC.MTGO.Common.Message;
using WotC.MtGO.Client.Model;
using MTGOSDK.API.Play;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.API.Events;

public sealed class Deck(dynamic deck) : CardGrouping<Deck>
{
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
          // Uses the default annotation (0) and permission code (215).
          (g, e) => new object[] { e.Id, (uint)0, 215, e.Quantity, g.Item2 })
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
      Lambda<DeckRegion>(e => Cast<DeckRegion>(e.EnumValue)));

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
    Unbind(this).GetRegionCount(GetRegionRef(region));

  /// <summary>
  /// Returns the cards in the specified region.
  /// </summary>
  /// <param name="region">The deck region to return cards from.</param>
  /// <returns>An iterator of CardQuantityPair objects.</returns>
  public IEnumerable<CardQuantityPair> GetCards(DeckRegion region) =>
    Map<CardQuantityPair>(Unbind(this).GetRegionCards(GetRegionRef(region)));

  // public void AddCards(...) => AddCardsToRegion(...)
  // public void RemoveCards(...) => RemoveCardsFromRegion(...)

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
