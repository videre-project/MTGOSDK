/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Tournaments;
using static MTGOSDK.API.Events;

public sealed class Tournament(dynamic tournament) : Event<Tournament>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ITournament);

  /// <summary>
  /// Stores an internal reference to the ITournament object.
  /// </summary>
  internal override dynamic obj => Bind<ITournament>(tournament);

  //
  // IQueueBasedEvent wrapper properties
  //

  /// <summary>
  /// The time the event is scheduled to start.
  /// </summary>
  public DateTime StartTime => @base.ScheduledStartTime;

  /// <summary>
  /// The time the event is scheduled to end.
  /// </summary>
  public DateTime EndTime => @base.ScheduledEndTime;

  /// <summary>
  /// The number of rounds in the tournament.
  /// </summary>
  public int TotalRounds => @base.TotalRounds;

  //
  // ITournament wrapper properties
  //

  /// <summary>
  /// The completion status of the tournament
  /// (i.e. "WaitingToStart", "RoundInProgress", etc.)
  /// </summary>
  public TournamentState State =>
    Cast<TournamentState>(Unbind(@base).State);

  /// <summary>
  /// The time remaining in the current round or tournament phase.
  /// </summary>
  public TimeSpan TimeRemaining =>
    State == TournamentState.BetweenRounds
      ? TimeSpan.Zero
      : Cast<TimeSpan>(Unbind(@base).TimeRemaining);

  /// <summary>
  /// The current round of the tournament.
  /// </summary>
  public int CurrentRound => @base.CurrentRoundNumber;

  /// <summary>
  /// Whether the user has a bye in the current round.
  /// </summary>
  public bool HasBye => @base.LocalUserHasByeInCurrentRound;

  /// <summary>
  /// Whether the tournament has progressed to playoffs (i.e. Top-8)
  /// </summary>
  public bool InPlayoffs => @base.IsInPlayoffs;

  /// <summary>
  /// The tournament's detailed round information.
  /// </summary>
  public IList<TournamentRound> Rounds =>
    Map<IList, TournamentRound>(@base.CurrentTournamentPart.Rounds);

  /// <summary>
  /// Standings for each player in the tournament.
  /// </summary>
  public IList<StandingRecord> Standings =>
    ((IEnumerable<StandingRecord>)Map<StandingRecord>(@base.Standings))
      .OrderByDescending(s => s.Rank)
      .ToList();

  //
  // ITournament wrapper events
  //

  public EventProxy<TournamentRoundChangedEventArgs> CurrentRoundChanged =
    new(/* ITournament */ tournament, nameof(CurrentRoundChanged));

  public EventProxy<TournamentStateChangedEventArgs> TournamentStateChanged =
    new(/* ITournament */ tournament, nameof(TournamentStateChanged));

  public EventProxy<TournamentErrorEventArgs> TournamentError =
    new(/* ITournament */ tournament, nameof(TournamentError));

  public EventProxy StandingsChanged =
    new(/* ITournament */ tournament, nameof(StandingsChanged));
}
