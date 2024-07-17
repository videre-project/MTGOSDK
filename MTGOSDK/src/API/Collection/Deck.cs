/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;

using WotC.MtGO.Client.Model;


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

  //
  // IDeck wrapper properties
  //

  public IEnumerable<DeckRegion> Regions =>
    Map<DeckRegion>(Unbind(@base).Regions,
      new Func<dynamic, DeckRegion>(e => Cast<DeckRegion>(e.EnumValue)));

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
    Unbind(@base).GetRegionCount(GetRegionRef(region));

  /// <summary>
  /// Returns the cards in the specified region.
  /// </summary>
  /// <param name="region">The deck region to return cards from.</param>
  /// <returns>An iterator of CardQuantityPair objects.</returns>
  public IEnumerable<CardQuantityPair> GetCards(DeckRegion region) =>
    Map<CardQuantityPair>(Unbind(@base).GetRegionCards(GetRegionRef(region)));

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
