/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Leagues;

public sealed class League(dynamic league) : Event<ILeague>
{
  /// <summary>
  /// Stores an internal reference to the ILeague object.
  /// </summary>
  internal override dynamic obj => Bind<ILeague>(league);

  /// <summary>
  /// Internal reference to the ILeagueLocalParticipant object.
  /// </summary>
  private ILeagueLocalParticipant LeagueUser => @base.LocalUserInLeague;

  //
  // ILeague wrapper properties
  //

  /// <summary>
  /// The name of the league event.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The date the league event was opened to new participants.
  /// </summary>
  public DateTime OpenDate => @base.OpenDate;

  /// <summary>
  /// The date the league event became active and matches began.
  /// </summary>
  public DateTime ActiveDate => @base.ActiveDate;

  /// <summary>
  /// The date the league event was closed to new participants.
  /// </summary>
  public DateTime ClosedDate => @base.ClosedDate;

  /// <summary>
  /// The date the league event was completed and the leaderboard finalized.
  /// </summary>
  public DateTime CompletedDate => @base.CompletedDate;

  /// <summary>
  /// The number of players who have joined the league.
  /// </summary>
  public int JoinedMembers => @base.JoinedMemberCount;

  /// <summary>
  /// The league's current leaderboard entries.
  /// </summary>
  public IEnumerable<LeaderboardEntry> Leaderboard =>
    Map<LeaderboardEntry>(@base.Leaderboard);

  /// <summary>
  /// The total number of matches playable in the league.
  /// </summary>
  public int TotalMatches => @base.TotalNumberOfMatches;

  /// <summary>
  /// The minimum number of matches required to be played in the league.
  /// </summary>
  public int MinMatches => @base.MinimumNumberOfMatches;

  /// <summary>
  /// Whether the league is currently inactive.
  /// </summary>
  public bool IsPaused => @base.IsPaused;

  //
  // ILeagueLocalParticipant wrapper properties
  //

  /// <summary>
  /// The user's chosen deck for the current league.
  /// </summary>
  public Deck ActiveDeck => new(LeagueUser.ActiveDeck);

  /// <summary>
  /// The game history of the current league.
  /// </summary>
  public IEnumerable<GameResult> GameHistory =>
    Map<GameResult>(@base.GameHistory);

  /// <summary>
  /// The current match number within the current league.
  /// </summary>
  public int MatchNumber => LeagueUser.CurrentMatchNumberWithinStage;

  /// <summary>
  /// The number of matches remaining in the current league.
  /// </summary>
  public int MatchesRemaining => LeagueUser.NumberOfRemainingMatches;

  /// <summary>
  /// The number of wins in the current league.
  /// </summary>
  public int Wins => LeagueUser.MatchWins;

  /// <summary>
  /// The number of losses in the current league.
  /// </summary>
  public int Losses => LeagueUser.MatchLosses;

  /// <summary>
  /// The number of trophies the user has earned in the league.
  /// </summary>
  public int TrophyCount => LeagueUser.TrophyCount;

  /// <summary>
  /// Whether the user is currently waiting in the match queue.
  /// </summary>
  public bool IsWaitingInMatchQueue => LeagueUser.IsWaitingInMatchQueue;

  /// <summary>
  /// Whether the user is currently in a match.
  /// </summary>
  public bool IsMatchInProgress => LeagueUser.IsMatchInProgress;
}
