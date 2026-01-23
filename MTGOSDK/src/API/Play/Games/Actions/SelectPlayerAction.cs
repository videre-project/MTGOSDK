/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class SelectPlayerAction(dynamic selectPlayerAction)
    : GameAction
{
  /// <summary>
  /// Stores an internal reference to the ISelectPlayerAction object.
  /// </summary>
  internal override dynamic obj =>
    Bind<ISelectPlayerAction>(selectPlayerAction);

  //
  // ISelectPlayerAction wrapper properties
  //

  public IList<GamePlayer> AvailablePlayers =>
    ((IEnumerable<dynamic>)
     Map<dynamic>(@base.AvailablePlayers))
      .Select(item => new GamePlayer(item.Value))
      .ToList();

  public GamePlayer SelectedPlayer =>
    new(@base.SelectedPlayer.Value);
}
