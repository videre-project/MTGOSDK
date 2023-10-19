/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MTGO.Common;


namespace MTGOSDK.API.Collection;

public sealed class Card(dynamic card) : DLRWrapper<ICardDefinition>
{
  /// <summary>
  /// Stores an internal reference to the ICardDefinition object.
  /// </summary>
  internal override dynamic obj => card;

  //
  // DigitalMagicObject properties
  //

  public int Id => @base.Id;

  public string Name => @base.Name;

  public int SourceId => @base.SourceId;

  //
  // ICardDefinition wrapper properties
  //

  public string Colors => @base.Colors;

  public string ManaCost => @base.ManaCost;

  public int ConvertedManaCost => @base.ConvertedManaCost;

  public string RulesText => @base.RulesText;

  // public CardType Type => Proxy<CardType>.As(@base.CardType); // TODO: cast enum

  public IList<string> Subtypes => @base.Subtypes;

  /// <summary>
  /// The name of the card's artist.
  /// </summary>
  public string Artist => @base.ArtistName;

  public int ArtId => @base.ArtId;

  public Set Set => new(Proxy<dynamic>.From(@base.CardSet));

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

  public static implicit operator string(Card card) => card.Name;
}
