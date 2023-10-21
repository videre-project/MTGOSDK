/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MTGO.Common;


namespace MTGOSDK.API.Collection;

public sealed class Deck(dynamic deck) : CardGrouping<IDeck>
{
  /// <summary>
  /// Stores an internal reference to the IDeck object.
  /// </summary>
  internal override dynamic obj => Proxy<IDeck>.As(deck);

  //
  // IDeck wrapper properties
  //

  public IEnumerable<DeckRegion> Regions
  {
    get
    {
      foreach (var region in Proxy<dynamic>.From(@base).Regions)
        yield return new DeckRegion(region);
    }
  }

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
    ((IEnumerable<ICardQuantityPair>)
      @base.GetCards(region))
        .Select(item => new CardQuantityPair(item));

  // public void AddCards(...) => AddCardsToRegion(...)
  // public void RemoveCards(...) => RemoveCardsFromRegion(...)

  // public override bool ItemIsLegalInThis(ICardDefinition card)
  // public static bool ItemIsLegalForDeck(ICardDefinition card, IPlayFormat playFormat = null)

  // public void ValidateCompanion()
  // {
  //   m_deck.ValidateCompanion();
  //   return m_deck.CompanionValidatorResults;
  // }
}
