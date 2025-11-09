/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Reflection;

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

  private Queue m_queue => field ??= new(tournament);

  //
  // IQueueBasedEvent wrapper properties
  //

  /// <summary>
  /// The available entry fee options for the tournament.
  /// </summary>
  public IList<EntryFeeSuite.EntryFee> EntryFee =>
    field ??= new EntryFeeSuite(Unbind(this).EntrySuite).EntryFees;

  /// <summary>
  /// The available prizes for the tournament, bracketed by final placement.
  /// </summary>
  public IDictionary<string, IList<EventPrize>> Prizes =>
    field ??= EventPrize.FromPrizeStructure(@base.Prizes, HasPlayoffs);

  public EventStructure EventStructure =>
    field ??= new(m_queue, Unbind(this).TournamentStructure);

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
      // Total number of rounds w/ playoffs (top 8) rounds.
      int realTotalRounds = TotalRounds + (HasPlayoffs ? 3 : 0);

      DateTime endTime = StartTime.AddMinutes(
        // Minutes per round + 2 minutes between rounds.
        (2 * Unbind(this).MatchTimeLimit * realTotalRounds) +
        (2 * (realTotalRounds - 1)) +
        // Minutes for deckbuilding.
        Try<int>(() => Unbind(this).MinutesForDeckbuilding)
      );

      // Round up to the nearest 10 minutes.
      return endTime.AddMinutes(10 - (endTime.Minute % 10));
    }
  }

  /// <summary>
  /// The number of rounds in the tournament.
  /// </summary>
  /// <remarks>
  /// This value is calculated based on the number of swiss rounds associated
  /// with the tournament, or based on the number of players if unavailable.
  /// </remarks>
  public int TotalRounds =>
    Math.Max(
      // Get the number of swiss rounds from the stored tournament data.
      Try(() => @base.TotalRounds - ((HasPlayoffs && InPlayoffs) ? 3 : 0),
          () => Unbind(this).SyncDataNumberOfRounds) ?? 0,
      // Fallback to calculating rounds based on the current player counts.
      Math.Max(GetNumberOfRounds(TotalPlayers),
               GetNumberOfRounds(MinimumPlayers))
    );

  //
  // ITournament wrapper properties
  //

  /// <summary>
  /// The completion status of the tournament
  /// (i.e. "WaitingToStart", "RoundInProgress", etc.)
  /// </summary>
  public TournamentState State =>
    Try(() => Cast<TournamentState>(Unbind(this).State),
        fallback: TournamentState.NotSet);

  /// <summary>
  /// The kind of elimination style used in the tournament
  /// (i.e. "Swiss", "SingleElimination")
  /// </summary>
  public TournamentEliminationStyle EliminationStyle =>
    Try(() => Cast<TournamentEliminationStyle>(
                  Unbind(this).TournamentEliminationStyle),
        fallback: TournamentEliminationStyle.Swiss);

  /// <summary>
  /// The time remaining in the current round or tournament phase.
  /// </summary>
  public TimeSpan TimeRemaining =>
    State == TournamentState.BetweenRounds
      ? TimeSpan.Zero
      : Cast<TimeSpan>(Unbind(this).TimeRemaining);

  /// <summary>
  /// The time at which the current round or tournament phase ends.
  /// </summary>
  /// <remarks>
  /// This returns the next round end time if between rounds, otherwise the
  /// next round time is estimated based on the match time limit and current
  /// server time.
  /// <para>
  /// If drafting, this will return the end of the draft phase.
  /// </para>
  /// </remarks>
  public DateTime RoundEndTime =>
    ServerTime.ServerTimeAsClientTime(@base.EndServerTime);

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
  public bool InPlayoffs => Try<bool>(() => @base.IsInPlayoffs);

  /// <summary>
  /// Whether the tournament has playoffs (i.e. Top-8) or concludes after swiss.
  /// </summary>
  public bool HasPlayoffs =>
    Try(() => Unbind(this).m_playoffs.Count > 0,
        () => this.EventStructure.HasPlayoffs) ?? false;

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

  public EventHookWrapper<TournamentRound> OnRoundChanged =
    new(RoundChanged, new Filter<TournamentRound>((s, _) => s.Id == tournament.Id));

  public EventHookWrapper<TournamentState> OnStateChanged =
    new(StateChanged, new Filter<TournamentState>((s, _) => s.Id == tournament.Id));

  public EventHookWrapper OnStandingsChanged =
    new(StandingsChanged, new Filter<dynamic>((s, _) => s.Id == tournament.Id));

  //
  // ITournament static events
  //

  public static EventHookProxy<Tournament, TournamentRound> RoundChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.TournamentEvent.Tournament>(),
      "OnCurrentRoundChanged",
      new((instance, args) =>
      {
        Tournament tournament = new(instance);
        TournamentRound newRound = new(args[1]);

        return (tournament, newRound);
      })
    );

  public static EventHookProxy<Tournament, TournamentState> StateChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.TournamentEvent.Tournament>(),
      "OnTournamentStateChanged",
      new((instance, args) =>
      {
        Tournament tournament = new(instance);

        var eventArgs = new TournamentStateChangedEventArgs(args);
        if (eventArgs.OldValue.Equals(TournamentState.Finished)) return null;
        TournamentState state = eventArgs.NewValue;

        return (tournament, state);
      })
    );

  public static EventHookProxy<Tournament, dynamic> StandingsChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.TournamentEvent.Tournament>(),
      "OnTournamentStateChanged",
      new((instance, _) =>
      {
        Tournament tournament = new(instance);
        return (tournament, null);
      })
    );
}
