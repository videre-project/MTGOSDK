/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Leagues;
using static MTGOSDK.API.Events;

public sealed class League(dynamic league) : Event<League>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ILeague);

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
  public IList<LeaderboardEntry> Leaderboard =>
    Map<IList, LeaderboardEntry>(@base.Leaderboard, proxy: true);

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
  public Deck? ActiveDeck => Optional<Deck>(LeagueUser.ActiveDeck);

  /// <summary>
  /// The game history of the current league.
  /// </summary>
  public IList<GameResult> GameHistory =>
    Map<IList, GameResult>(LeagueUser.GameHistory);

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

  //
  // ILeague wrapper methods
  //

  public override string ToString() => $"{Name} #{Id}";

  //
  // ILeague wrapper events
  //

  public EventProxy ActiveDateChanged =
    new(/* ILeague */ league, nameof(ActiveDateChanged));

  public EventProxy ClosedDateChanged =
    new(/* ILeague */ league, nameof(ClosedDateChanged));

  public EventProxy CompletedDateChanged =
    new(/* ILeague */ league, nameof(CompletedDateChanged));

  public EventProxy DescriptionChanged =
    new(/* ILeague */ league, nameof(DescriptionChanged));

  public EventProxy EventLinkChanged =
    new(/* ILeague */ league, nameof(EventLinkChanged));

  public EventProxy<LeagueOperationEventArgs> JoinCompleted =
    new(/* ILeague */ league, nameof(JoinCompleted));

  public EventProxy<LeagueOperationEventArgs> LeaveCompleted =
    new(/* ILeague */ league, nameof(LeaveCompleted));

  public EventProxy<LeagueOperationEventArgs> LeaderboardReceived =
    new(/* ILeague */ league, nameof(LeaderboardReceived));

  public EventProxy LeagueEntryOptionsChanged =
    new(/* ILeague */ league, nameof(LeagueEntryOptionsChanged));

  public EventProxy<LeagueStateEventArgs> StateChanged =
    new(/* ILeague */ league, nameof(StateChanged));
}
