/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Tournaments;
using static MTGOSDK.API.Events;

public sealed class Tournament(dynamic tournament) : Event
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
  /// The available entry fee options for the tournament.
  /// </summary>
  public IList<EntryFeeSuite.EntryFee> EntryFee =>
    new EntryFeeSuite(Unbind(@base).EntrySuite).EntryFees;

  /// <summary>
  /// The available prizes for the tournament, bracketed by final placement.
  /// </summary>
  public IDictionary<string, IList<EventPrize>> Prizes =>
    EventPrize.FromPrizeStructure(@base.Prizes, HasPlayoffs);

  /// <summary>
  /// The time the event is scheduled to start.
  /// </summary>
  public DateTime StartTime => @base.ScheduledStartTime;

  /// <summary>
  /// The time the event is scheduled to end.
  /// </summary>
  /// <remarks>
  /// This is a rough approximation of the end time, based on the current number
  /// of players in the tournament. The actual end time may be earlier than this
  /// time if a round finishes early.
  /// </remarks>
  public DateTime EndTime
  {
    get
    {
      int realTotalRounds = TotalRounds + (HasPlayoffs ? 3 : 0);
      DateTime endTime = StartTime.AddMinutes(
        // Minutes per round + 2 minutes between rounds.
        (2 * Unbind(@base).MatchTimeLimit * realTotalRounds) +
        (2 * (realTotalRounds - 1)) +
        // Minutes for deckbuilding.
        Try<int>(() => Unbind(@base).MinutesForDeckbuilding)
      );

      // Round up to the nearest 10 minutes.
      return endTime.AddMinutes(10 - (endTime.Minute % 10));
    }
  }

  /// <summary>
  /// The number of rounds in the tournament.
  /// </summary>
  public int TotalRounds =>
    Try<int>(() =>
      EliminationStyle == TournamentEliminationStyle.Swiss
        ? Math.Max(Try<int>(() => @base.TotalRounds),
                   Math.Max(GetNumberOfRounds(TotalPlayers),
                            GetNumberOfRounds(MinimumPlayers)))
        : @base.TotalRounds)
    // Remove the top 8 rounds from the swiss count.
    - ((HasPlayoffs && InPlayoffs) ? 3 : 0);

  //
  // ITournament wrapper properties
  //

  /// <summary>
  /// The completion status of the tournament
  /// (i.e. "WaitingToStart", "RoundInProgress", etc.)
  /// </summary>
  public TournamentState State =>
    Try(() => Cast<TournamentState>(Unbind(@base).State),
        fallback: TournamentState.NotSet);

  /// <summary>
  /// The kind of elimination style used in the tournament
  /// (i.e. "Swiss", "SingleElimination")
  /// </summary>
  public TournamentEliminationStyle EliminationStyle =>
    Try(() => Cast<TournamentEliminationStyle>(
                  Unbind(@base).TournamentEliminationStyle),
        fallback: TournamentEliminationStyle.Swiss);

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
  /// Whether the tournament has playoffs (i.e. Top-8) or concludes after swiss.
  /// </summary>
  public bool HasPlayoffs => Unbind(@base).m_playoffs.Count > 0;

  /// <summary>
  /// The tournament's detailed round information.
  /// </summary>
  [NonSerializable]
  public IList<TournamentRound> Rounds =>
    Map<IList, TournamentRound>(@base.CurrentTournamentPart.Rounds);

  /// <summary>
  /// Standings for each player in the tournament.
  /// </summary>
  [NonSerializable]
  public IList<StandingRecord> Standings =>
    ((IEnumerable<StandingRecord>)Map<StandingRecord>(@base.Standings))
      .OrderBy(s => s.Rank)
      .ToList();

  //
  // ITournament wrapper methods
  //

  /// <summary>
  /// Computes the number of rounds for a given size tournament.
  /// </summary>
  /// <param name="players">The number of players in the tournament.</param>
  /// <returns>The number of rounds required for the given number of players.</returns>
  public static int GetNumberOfRounds(int players) =>
    players switch
    {
      >=   2 and <=   3 => 2,
      >=   4 and <=   8 => 3,
      >=   9 and <=  16 => 4,
      >=  17 and <=  32 => 5,
      >=  33 and <=  64 => 6,
      >=  65 and <= 128 => 7,
      >= 129 and <= 226 => 8,
      >= 227 and <= 409 => 9,
      >= 410 => 10,
      _ => 1
    };

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
