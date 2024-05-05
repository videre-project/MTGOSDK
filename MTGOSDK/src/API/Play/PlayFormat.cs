/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MTGO.Common;
using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Play;

public sealed class PlayFormat(dynamic playFormat) : DLRWrapper<IPlayFormat>
{
  /// <summary>
  /// Stores an internal reference to the IPlayFormat object.
  /// </summary>
  internal override dynamic obj => playFormat;

  //
  // IPlayFormat wrapper properties
  //

  /// <summary>
  /// The name of the format.
  /// </summary>
  public string Name => @base.Name;

  public string Code => @base.Code;

  /// <summary>
  /// The minimum number of cards that can be in a deck.
  /// </summary>
  public int MinDeckSize => @base.MinimumDeckSize;

  /// <summary>
  /// The maximum number of cards that can be in a deck.
  /// </summary>
  public int MaxDeckSize => @base.MaximumDeckSize;

  /// <summary>
  /// The maximum number of copies of a card that can be in a deck.
  /// </summary>
  public int MaxCopiesPerCard => @base.MaximumCopiesOfACardPerDeck;

  /// <summary>
  /// The maximum number of cards that can be in the sideboard.
  /// </summary>
  public int MaxSideboardSize => @base.MaximumSideboardSize;

  /// <summary>
  /// The format type (i.e. Constructed, Sealed, Draft).
  /// </summary>
  internal string Type => Unbind(@base).Type.ToString();

  /// <summary>
  /// The sets that are legal in this format.
  /// </summary>
  public IEnumerable<Set> LegalSets =>
    Map<Set>(Unbind(@base).LegalSetsByCode.Values);

  /// <summary>
  /// Basic land cards that can be used for deckbuilding.
  /// </summary>
  public IEnumerable<Card> BasicLands =>
    Map<Card>(Unbind(@base).BasicLandsForDeckbuilding);

  /// <summary>
  /// The internal enum flag value of the format game structure.
  /// </summary>
  /// <remarks>
  /// Requires the <c>WotC.MTGO.Common</c> reference assembly.
  /// </remarks>
  internal GameStructureEnum EnumValue =>
    Cast<GameStructureEnum>(Unbind(@base).GameStructureEnum);

  //
  // IPlayFormat wrapper methods
  //

  /// <summary>
  /// Checks if a card is legal in this format.
  /// </summary>
  /// <param name="catalogId">The catalog ID of the card to check.</param>
  /// <returns>True if the card is legal, false otherwise.</returns>
  public bool IsCardLegal(int catalogId) =>
    @base.IsLegal(catalogId);

  /// <summary>
  /// Checks if a card is restricted in this format.
  /// </summary>
  /// <param name="catalogId">The catalog ID of the card to check.</param>
  /// <returns>True if the card is restricted, false otherwise.</returns>
  public bool IsCardRestricted(int catalogId) =>
    @base.IsCardRestricted(catalogId);

  /// <summary>
  /// Checks if a card is banned in this format.
  /// </summary>
  /// <param name="catalogId">The catalog ID of the card to check.</param>
  /// <returns>True if the card is banned, false otherwise.</returns>
  public bool IsCardBanned(int catalogId) =>
    @base.IsCardBanned(catalogId);

  /// <summary>
  /// Checks if a deck is legal in this format.
  /// </summary>
  /// <param name="deck">The deck object to check.</param>
  /// <returns>True if the deck is legal, false otherwise.</returns>
  public bool IsDeckLegal(Deck deck) =>
    @base.CheckDeckLegality(/* IDeck */ deck.@base, /* ignoreErrors */ false);

  /// <summary>
  /// Sets the format legality of a deck to match this format.
  /// </summary>
  /// <param name="deck">The deck object to set legality on.</param>
  public void SetDeckLegality(Deck deck) =>
    @base.SetDeckLegality(/* IDeck */ deck.@base);

  public override string ToString() => this.Name;
}
