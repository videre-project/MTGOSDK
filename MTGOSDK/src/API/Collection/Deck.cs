/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MTGO.Common;


namespace MTGOSDK.API.Collection;

public sealed class Deck(dynamic deck) : CollectionItem<IDeck>
{
  /// <summary>
  /// Stores an internal reference to the IDeck object.
  /// </summary>
  internal override dynamic obj => deck;

  //
  // IDeck wrapper properties
  //

  public DeckRegion[] Regions => Proxy<DeckRegion[]>.As(@base.Regions);

  /// <summary>
  /// The unique identifier for this deck used for matchmaking.
  /// </summary>
  public int DeckId => @base.PreconstuctedDeckId;

  /// <summary>
  /// Whether the deck is legal for play.
  /// </summary>
  public bool IsLegal => @base.IsLegal;

  //
  // IDeck wrapper methods
  //

  // public void AddCardsToRegion(...)
  // public void RemoveCardsFromRegion(...)

  // public void ValidateCompanion()
  // {
  //   m_deck.ValidateCompanion();
  //   return m_deck.CompanionValidatorResults;
  // }
}
