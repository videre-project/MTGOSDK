/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

public sealed class Card(dynamic card) : CollectionItem<ICardDefinition>
{
  /// <summary>
  /// Stores an internal reference to the ICardDefinition object.
  /// </summary>
  internal override dynamic obj => card;

  //
  // ICardDefinition wrapper properties
  //

  /// <summary>
  /// A string representing the card's unique colors (e.g. "WUBRG").
  /// </summary>
  public string Colors => @base.Colors;

  /// <summary>
  /// The mana cost of the card.
  /// </summary>
  public string ManaCost => @base.ManaCost;

  /// <summary>
  /// The card's converted mana cost (or mana value).
  /// </summary>
  public int ConvertedManaCost => @base.ConvertedManaCost;

  /// <summary>
  /// The card's oracle text.
  /// </summary>
  public string RulesText => @base.RulesText;

  /// <summary>
  /// A list of the card's types.
  /// </summary>
  public IList<string> Types =>
    ((string)Unbind(@base).Types.ToString())
      .Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
      .ToList();

  /// <summary>
  /// A list of the card's subtypes.
  /// </summary>
  public IList<string> Subtypes => @base.Subtypes;

  /// <summary>
  /// The name of the card's artist.
  /// </summary>
  public string Artist => @base.ArtistName;

  /// <summary>
  /// The unique identifier for the card's art.
  /// </summary>
  public int ArtId => @base.ArtId;

  /// <summary>
  /// The card's printed set.
  /// </summary>
  public Set Set => new(Unbind(@base.CardSet));

  /// <summary>
  /// A string representing the card's collector info (e.g. "1/254").
  /// </summary>
  public string CollectorInfo => @base.CollectorInfo;

  /// <summary>
  /// The card's collector number.
  /// </summary>
  public int CollectorNumber => @base.CollectorNumber;

  /// <summary>
  /// The printed flavor text for the card.
  /// </summary>
  public string FlavorText => @base.FlavorText;

  /// <summary>
  /// The card's power.
  /// </summary>
  public string Power => @base.Power;

  /// <summary>
  /// The card's toughness.
  /// </summary>
  public string Toughness => @base.Toughness;

  /// <summary>
  /// The card's initial loyalty.
  /// </summary>
  public string Loyalty => @base.InitialLoyalty;

  /// <summary>
  /// The card's initial battle defense.
  /// </summary>
  public string Defense => @base.InitialBattleDefense;

  //
  // ICardDefinition wrapper methods
  //

  public static implicit operator int(Card card) => card.Id;

  public static implicit operator string(Card card) => card.Name;
}
