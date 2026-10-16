/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class ManaPoolAction(dynamic manaPoolAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the IManaPoolAction object.
  /// </summary>
  internal override dynamic obj => Bind<IManaPoolAction>(manaPoolAction);

  //
  // IManaPoolAction wrapper properties
  //

  public MagicColors ManaPoolColors => Cast<MagicColors>(@base.ManaPoolColors);

  public MagicColors SelectedItem => Cast<MagicColors>(@base.SelectedItem);

  public int SelectedID => @base.SelectedID;
}
