/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class Mana(dynamic manaItem) : DLRWrapper<IManaPoolItem>
{
  internal override dynamic obj =>
    manaItem is GamePlayerPartial.ManaPartial partial
      ? partial
      : Bind<IManaPoolItem>(manaItem);

  /// <summary>
  /// The unique identifier for the given mana type.
  /// </summary>
  [NonSerializable]
  public int ID => @base.ID;

  /// <summary>
  /// The type of color(s) the mana represents.
  /// </summary>
  [NonSerializable]
  public MagicColors Color => Cast<MagicColors>(Unbind(this).Color);

  /// <summary>
  /// The amount of the given mana type in the player's mana pool.
  /// </summary>
  public int Amount => @base.Amount;

  /// <summary>
  /// The mana color represented as one or more symbol tokens.
  /// </summary>
  public string Symbol => ToSymbol(Color);

  private static string ToSymbol(MagicColors color) => color switch
  {
    MagicColors.Invalid   => string.Empty,

    MagicColors.White     => "{W}",
    MagicColors.Blue      => "{U}",
    MagicColors.Black     => "{B}",
    MagicColors.Red       => "{R}",
    MagicColors.Green     => "{G}",
    MagicColors.Colorless => "{C}",

    MagicColors.Azorius  => "{W}{U}",
    MagicColors.Orzhov   => "{W}{B}",
    MagicColors.Dimir    => "{U}{B}",
    MagicColors.Izzet    => "{U}{R}",
    MagicColors.Rakdos   => "{R}{B}",
    MagicColors.Golgari  => "{B}{G}",
    MagicColors.Gruul    => "{R}{G}",
    MagicColors.Boros    => "{R}{W}",
    MagicColors.Selesnya => "{G}{W}",
    MagicColors.Simic    => "{G}{U}",

    MagicColors.Abzan  => "{W}{B}{G}",
    MagicColors.Jeskai => "{U}{R}{W}",
    MagicColors.Mardu  => "{R}{W}{B}",
    MagicColors.Temur  => "{G}{U}{R}",
    MagicColors.Sultai => "{B}{G}{U}",
    MagicColors.Naya   => "{R}{G}{W}",
    MagicColors.Jund   => "{B}{R}{G}",
    MagicColors.Grixis => "{U}{B}{R}",
    MagicColors.Esper  => "{W}{U}{B}",
    MagicColors.Bant   => "{G}{W}{U}",

    MagicColors.WUBR => "{W}{U}{B}{R}",
    MagicColors.UBRG => "{U}{B}{R}{G}",
    MagicColors.BRGW => "{B}{R}{G}{W}",
    MagicColors.RGWU => "{R}{G}{W}{U}",
    MagicColors.GWUB => "{G}{W}{U}{B}",

    MagicColors.FiveColor => "{W}{U}{B}{R}{G}",

    _ => string.Concat(
      color.HasFlag(MagicColors.White)     ? "{W}" : string.Empty,
      color.HasFlag(MagicColors.Blue)      ? "{U}" : string.Empty,
      color.HasFlag(MagicColors.Black)     ? "{B}" : string.Empty,
      color.HasFlag(MagicColors.Red)       ? "{R}" : string.Empty,
      color.HasFlag(MagicColors.Green)     ? "{G}" : string.Empty,
      color.HasFlag(MagicColors.Colorless) ? "{C}" : string.Empty)
  };
}
