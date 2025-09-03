/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class Mana(dynamic manaItem) : DLRWrapper<IManaPoolItem>
{
  internal override dynamic obj => Bind<IManaPoolItem>(manaItem);

  /// <summary>
  /// The unique identifier for the given mana type.
  /// </summary>
  public int ID => @base.ID;

  /// <summary>
  /// The type of color(s) the mana represents.
  /// </summary>
  public MagicColors Color => Cast<MagicColors>(Unbind(this).Color);

  /// <summary>
  /// The amount of the given mana type in the player's mana pool.
  /// </summary>
  public int Amount => @base.Amount;
}
