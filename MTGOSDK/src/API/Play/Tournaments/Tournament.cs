/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;

using MTGOSDK.API.Users;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model.Play;
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

  private static readonly ConcurrentDictionary<int, StandingTable> s_standingTables = new();

  private StandingTable m_standingTable =>
    s_standingTables.GetOrAdd(Id, _ => new StandingTable());

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

  /// <summary>
  /// The event structure of the tournament.
  /// </summary>
  public EventStructure EventStructure =>
    field ??= new(m_queue, Unbind(this).TournamentStructure);

  /// <summary>
  /// The players who are no longer active in the tournament.
  /// </summary>
  public IEnumerable<User> EliminatedPlayers =>
    Try(() => Map<User>(Unbind(this).EliminatedUsers).ToArray(),
        fallback: Array.Empty<User>());

  /// <summary>
  /// The players still active in the tournament.
  /// </summary>
  public IEnumerable<User> ActivePlayers
  {
    get
    {
      var eliminatedNames = EliminatedPlayers
        .Select(player => player.Name)
        .ToHashSet(StringComparer.Ordinal);

      return Players
        .Where(player => !eliminatedNames.Contains(player.Name))
        .ToArray();
    }
  }

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
  /// <para>
  /// This assumes that each round takes the full match time limit to complete,
  /// and that there is a 2 minute break between rounds (no fast-rounds).
  /// </para>
  /// </remarks>
  public DateTime EndTime
  {
    get
    {
      // Total number of rounds w/ playoffs (top 8) rounds.
      int realTotalRounds = TotalRounds + (HasPlayoffs ? 3 : 0);

      DateTime endTime = StartTime.AddMinutes(
        // Minutes per round + 2 minutes between rounds.
        (TotalRoundDuration.TotalMinutes * realTotalRounds) +
        (2 * (realTotalRounds - 1)) +
        // Minutes for deckbuilding.
        Try<int>(() => Unbind(this).MinutesForDeckbuilding)
      );

      // Round up to the nearest 10 minutes.
      return endTime.AddMinutes(10 - (endTime.Minute % 10));
    }
  }

  /// <summary>
  /// The total expected duration of a single round in the tournament.
  /// </summary>
  /// <remarks>
  /// This includes the match time limit for both players, sideboarding time,
  /// and a small buffer to account for clock drift from priority exchanges.
  /// <para>
  /// Note that this does not account for game resets, which can cause an
  /// extended round.
  /// </para>
  /// </remarks>
  [NonSerializable]
  public TimeSpan TotalRoundDuration =>
    TimeSpan.FromMinutes(
      // The match time limit applies for both players, meaning it is doubled.
      (2 * (double)Unbind(this).MatchTimeLimit) +
      // Bo3 games can have at most 2 sideboard periods of 3 minutes each.
      (2 * 3.0) +
      // Per MTGO_Tony, as fractions of a second are added to players' clocks
      // when exchanging priority, we can have an additional 2 minutes added.
      2.0
    );

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
    ServerTime.ServerTimeAsClientTime(this.State switch
    {
      // Uses the same server-side calculation of the round end time, based on
      // the total match time limit for both players plus sideboarding time.
      TournamentState.RoundInProgress
        => Unbind(this).CurrentRound.StartTime + this.TotalRoundDuration,
      // Fallback to the computed end server time.
      _ => Unbind(this).EndServerTime
    });

  /// <summary>
  /// The current round of the tournament.
  /// </summary>
  public int RoundNumber
  {
    get
    {
      int currentRoundNumber = Try(() => @base.CurrentRoundNumber, fallback: 0);
      int currentRoundObjectNumber = Try(() => CurrentRound?.Number ?? 0, fallback: 0);

      return Math.Max(currentRoundNumber, currentRoundObjectNumber);
    }
  }

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
  /// The user's current active match in the tournament round.
  /// </summary>
  [NonSerializable]
  public Match? ActiveMatch
  {
    get
    {
      return Try(() =>
      {
        var currentRoundObj = Unbind(this).CurrentRound;
        if (currentRoundObj is null) return null;

        dynamic userMatchRemote = ((DynamicRemoteObject)currentRoundObj.Matches)
          .Filter<IPlayerEvent>(m => m.IsCompleted == false)
          .Filter<IPlayerEvent>(m => m.IsLocalUserJoined == true);

        return userMatchRemote.Count > 0
          ? Optional<Match>(userMatchRemote[0])
          : null;
      }, fallback: null);
    }
  }

  /// <summary>
  /// The tournament's detailed round information.
  /// </summary>
  [NonSerializable]
  public TournamentRound? CurrentRound =>
    Try(() => Optional<TournamentRound>(Unbind(this).CurrentRound),
        fallback: null);

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

  /// <summary>
  /// Computes a lightweight hash of the current round's in-progress match state.
  /// </summary>
  /// <param name="includeTournamentId">
  /// Whether to include the tournament ID for globally unique hash keys.
  /// </param>
  /// <returns>
  /// The round hash, or <c>null</c> when the current round state cannot be read.
  /// </returns>
  public int? GetRoundHash(bool includeTournamentId = false)
  {
    try
    {
      TournamentRound? round = CurrentRound;
      if (round is null)
      {
        return null;
      }

      int[] matchHashes = round.GetRoundHashMatchHashes();
      return ComputeRoundHash(
        includeTournamentId ? Id : null,
        round.Number,
        matchHashes.Length,
        matchHashes);
    }
    catch
    {
      return null;
    }
  }

  private static int ComputeRoundHash(
    int? tournamentId,
    int roundNumber,
    int matchCount,
    IEnumerable<int> matchHashes)
  {
    var hc = new HashCode();
    if (tournamentId.HasValue)
    {
      hc.Add(tournamentId.Value);
    }

    hc.Add(roundNumber);
    hc.Add(matchCount);
    foreach (int matchHash in matchHashes)
    {
      hc.Add(matchHash);
    }

    return hc.ToHashCode();
  }

  /// <summary>
  /// Compute the live standings for the tournament, including match and game
  /// win/loss/draw records.
  /// </summary>
  /// <remarks>
  /// This method computes the standings based on the current match data for
  /// each player, and applies the standard MTGO tie-breaking rules based on
  /// match points, opponent match win percentage, game win percentage, and
  /// opponent game win percentage.
  /// <para>
  /// This is necessary because the MTGO client does not compute or expose
  /// match/game records or tie-breaking percentages until the end of the
  /// round, which can lead to stale or inaccurate standings data during the
  /// round.
  /// </para>
  /// </remarks>
  /// <returns>
  /// A list of <see cref="StandingRecord"/> objects, sorted by rank in
  /// ascending order.
  /// </returns>
  /// <exception cref="InvalidOperationException">
  /// Thrown if the tournament is not a Swiss or playoff tournament.
  /// </exception>
  public IList<StandingRecord> ComputeStandings()
    => m_standingTable.ComputeStandings(this);

  //
  // ITournament wrapper events
  //

  private static readonly ConcurrentDictionary<int, int[]> s_standingsHashTable = new();

  private static int GetEventId(object tournament) =>
    tournament is Event e
      ? e.Id
      : Try<int>(() => ((dynamic)tournament).EventId);

  public EventHookWrapper<IList<StandingRecord>> OnStandingsChanged =
    new EventHookWrapper<IList<StandingRecord>>(StandingsChanged, new((s, arr) =>
    {
      int tournamentId = s.Id;
      if (tournamentId != GetEventId(tournament)) return false;

      long totalStart = Stopwatch.GetTimestamp();
      long phaseStart = Stopwatch.GetTimestamp();
      var standings = ((Tournament)s).ComputeStandings();
      double computeMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;

      // Compute the hash for each standing
      phaseStart = Stopwatch.GetTimestamp();
      int[] newHashes = standings.Select(r => r.ToString().GetHashCode()).ToArray();
      s_standingsHashTable.TryRemove(tournamentId, out int[] oldHashes);
      oldHashes ??= Array.Empty<int>();
      s_standingsHashTable.TryAdd(tournamentId, newHashes);

      for (int i = 0; i < newHashes.Length; i++)
      {
        if (i >= oldHashes.Length || newHashes[i] != oldHashes[i])
        {
          arr.Add(standings[i]);
        }
      }
      double deltaFingerprintMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;

      Log.Information(
        "[StandingsTelemetry] standings-changed tournament={TournamentId} " +
        "standings={StandingsCount} emitted={DeltaCount} " +
        "computeMs={ComputeMs:0.000} deltaFingerprintMs={DeltaFingerprintMs:0.000} " +
        "totalMs={TotalMs:0.000}",
        tournamentId,
        standings.Count,
        arr.Count,
        computeMs,
        deltaFingerprintMs,
        Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds);

      return arr.Count > 0;
    })).OnInitialize(() =>
    {
      // Construct the initial standings in a dummy Tournament object first.
      var standings = new Tournament(tournament).ComputeStandings();

      int[] newHashes = standings.Select(r => r.ToString().GetHashCode()).ToArray();
      s_standingsHashTable.TryAdd(GetEventId(tournament), newHashes);
    });

  public EventHookWrapper<TournamentRound> OnRoundChanged =
    new(RoundChanged, new Filter<TournamentRound>((s, _) => s.Id == GetEventId(tournament)));

  public EventHookWrapper<TournamentState> OnStateChanged =
    new(StateChanged, new Filter<TournamentState>((s, _) => s.Id == GetEventId(tournament)));

  //
  // ITournament static events
  //

  public static EventHookProxy<Tournament, IList<StandingRecord>> StandingsChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.TournamentEvent.Tournament>(),
      "OnStandingsChanged",
      new((instance, args) =>
      {
        Tournament tournament = new(instance);
        return (tournament, new List<StandingRecord>());
      }),
      HarmonyPatchPosition.Postfix
    );

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

        var eventArgs = new TournamentStateChangedEventArgs(args[0]);
        if (eventArgs.OldValue.Equals(TournamentState.Finished)) return null;
        TournamentState state = eventArgs.NewValue;

        return (tournament, state);
      })
    );

  public static void EnsureHooksInitialized()
  {
    StandingsChanged.EnsureInitialize();
    RoundChanged.EnsureInitialize();
    StateChanged.EnsureInitialize();
  }
}
