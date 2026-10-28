/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Leagues;

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
  public User Opponent => new((string)@base.OpponentName);

  /// <summary>
  /// The type of game result (None, Win, Loss, Draw)
  /// </summary>
  public string Result => Unbind(this).GameResult.ToString();

  /// <summary>
  /// Whether MTGO reports a win for this match.
  /// </summary>
  public bool IsWin => @base.MatchResultIsWin;

  /// <summary>
  /// The player's match-clock usage, in minutes, when this game ended.
  /// </summary>
  /// <remarks>
  /// Includes time used in earlier games of the match.
  /// </remarks>
  public int MinutesPlayed => @base.MinutesPlayed;

  /// <summary>
  /// The match number in the player's league entry, starting at one.
  /// </summary>
  /// <remarks>
  /// Numbering continues across stages.
  /// </remarks>
  public int MatchNumber =>
    League.GetMatchNumber(
      Bind<ILeague>(@base.League),
      Bind<ILeagueStage>(@base.StageMatch.Parent),
      @base.StageMatch.Index
    );

  /// <summary>
  /// The time of the last recorded match result override.
  /// </summary>
  public DateTime MatchModificationTime => @base.MatchModificationTime;

  /// <summary>
  /// The change applied to this match's recorded result.
  /// </summary>
  public MatchResultOverride ResultOverride =>
    Cast<MatchResultOverride>(Unbind(this).MatchModificationState);
}
