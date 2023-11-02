/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Events.Leagues;

public sealed class GameResult(dynamic leagueGameResult)
    : DLRWrapper<ILeagueGameResult>
{
  /// <summary>
  /// Stores an internal reference to the ILeagueGameResult object.
  /// </summary>
  internal override dynamic obj => leagueGameResult;

  //
  // ILeagueGameResult wrapper properties
  //

  /// <summary>
  /// The match ID of the game result.
  /// </summary>
  public int MatchId => @base.MatchId;

  /// <summary>
  /// The game ID of the game result.
  /// </summary>
  public int GameId => @base.GameId;

  /// <summary>
  /// The game index within the match.
  /// </summary>
  public int GameIndex => @base.GameIndex;

  /// <summary>
  /// The opponent user who played the game.
  /// </summary>
  public User Opponent => new(@base.Opponent);

  /// <summary>
  /// The type of game result (None, Win, Loss, Draw)
  /// </summary>
  public string Result => Unbind(@base).GameResult.ToString();

  /// <summary>
  /// The last time the game result was modified.
  /// </summary>
  public DateTime MatchModificationTime => @base.MatchModificationTime;

  /// <summary>
  /// The number of minutes played in the game.
  /// </summary>
  public int MinutesPlayed => @base.MinutesPlayed;

  /// <summary>
  /// Whether the game result is a win for the current user.
  /// </summary>
  public bool IsWin => @base.MatchResultIsWin;
}
