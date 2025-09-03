/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

[NonSerializable]
public sealed class Card(dynamic card)
    // We override the base instance with the ICardDefinition interface.
    : CollectionItem<Card>(null)
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ICardDefinition);

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
  public string Colors => field ??= @base.ColorDisplayString;

  /// <summary>
  /// The mana cost of the card.
  /// </summary>
  public string ManaCost => field ??= @base.ManaCost;

  /// <summary>
  /// The card's converted mana cost (or mana value).
  /// </summary>
  public int ConvertedManaCost => @base.ConvertedManaCost;

  /// <summary>
  /// The card's oracle text.
  /// </summary>
  public string RulesText => field ??= @base.RulesText;

  /// <summary>
  /// A list of the card's types.
  /// </summary>
  public IList<string> Types =>
    field ??= Map<IList, string>(
      Unbind(this).Types
        .ToString()
        .Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries));

  /// <summary>
  /// A list of the card's subtypes.
  /// </summary>
  public IList<string> Subtypes =>
    field ??= Map<IList, string>(@base.Subtypes);

  /// <summary>
  /// The name of the card's artist.
  /// </summary>
  public string Artist => field ??= @base.ArtistName;

  /// <summary>
  /// The unique identifier for the card's art.
  /// </summary>
  public int ArtId => @base.ArtId;

  /// <summary>
  /// The card's printed set.
  /// </summary>
  public Set Set => field ??= new(Unbind(@base.CardSet));

  /// <summary>
  /// A string representing the card's collector info (e.g. "1/254").
  /// </summary>
  public string CollectorInfo => field ??= @base.CollectorInfo;

  /// <summary>
  /// The card's collector number.
  /// </summary>
  public int CollectorNumber => @base.CollectorNumber;

  /// <summary>
  /// The printed flavor text for the card.
  /// </summary>
  public string FlavorText => field ??= @base.FlavorText;

  /// <summary>
  /// The card's power.
  /// </summary>
  public string Power => field ??= @base.Power;

  /// <summary>
  /// The card's toughness.
  /// </summary>
  public string Toughness => field ??= @base.Toughness;

  /// <summary>
  /// The card's initial loyalty.
  /// </summary>
  public string Loyalty => field ??= @base.InitialLoyalty;

  /// <summary>
  /// The card's initial battle defense.
  /// </summary>
  public string Defense => field ??= @base.InitialBattleDefense;

  /// <summary>
  /// Whether the card represents a token.
  /// </summary>
  public bool IsToken => @base.IsToken;

  //
  // ICardDefinition wrapper methods
  //

  public override string ToString() => this.Name;

  public static implicit operator int(Card card) => card.Id;

  public static implicit operator string(Card card) => card.ToString();
}
