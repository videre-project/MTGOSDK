/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.API.Events;

public sealed class Deck(dynamic deck) : CardGrouping<Deck>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  internal override Type type => typeof(IDeck);

  /// <summary>
  /// Stores an internal reference to the IDeck object.
  /// </summary>
  internal override dynamic obj => Bind<IDeck>(deck);

  //
  // IDeck wrapper properties
  //

  public IEnumerable<DeckRegion> Regions =>
    Map<DeckRegion>(Unbind(@base).Regions);

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
  /// Returns the number of cards in the specified region.
  /// </summary>
  /// <param name="region">The deck region to count cards in.</param>
  public int GetRegionCount(DeckRegion region) =>
    @base.GetRegionCount(region);

  /// <summary>
  /// Returns the cards in the specified region.
  /// </summary>
  /// <param name="region">The deck region to return cards from.</param>
  /// <returns>An iterator of CardQuantityPair objects.</returns>
  public IEnumerable<CardQuantityPair> GetCards(DeckRegion region) =>
    Map<CardQuantityPair>(@base.GetCards(region));

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
