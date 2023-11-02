/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MTGO.Common;
using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Events.Tournaments;

public sealed class Tournament(dynamic tournament) : Event<ITournament>
{
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
  /// The completion status of the tournament (i.e. "WaitingToStart", "RoundInProgress", etc.)
  /// </summary>
  /// <remarks>
  /// Requires the <c>WotC.MTGO.Common</c> reference assembly.
  /// </remarks>
  public TournamentStateEnum State =>
    Cast<TournamentStateEnum>(Unbind(@base).State.EnumValue);

  /// <summary>
  /// The time remaining in the current round or tournament phase.
  /// </summary>
  public TimeSpan TimeRemaining => @base.TimeRemaining;

  /// <summary>
  ///
  /// </summary>
  public string Status => @base.TournamentStatus;

  /// <summary>
  /// The current round of the tournament.
  /// </summary>
  public int CurrentRound => @base.CurrentRound;

  /// <summary>
  /// Whether the user has a bye in the current round.
  /// </summary>
  public bool HasBye => @base.LocalUserHasByeInCurrentRound;

  /// <summary>
  /// Whether the tournament has progressed to playoffs (i.e. Top-8)
  /// </summary>
  public bool InPlayoffs => @base.IsInPlayoffs;

  /// <summary>
  /// Standings for each player in the tournament.
  /// </summary>
  public IEnumerable<StandingRecord> Standings =>
    Map<StandingRecord>(@base.Standings);
}
