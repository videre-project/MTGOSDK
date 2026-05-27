/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Globalization;

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Tournaments;

internal sealed class StandingTable
{
  private const int PlayoffCut = 8;

  // Temporary verification: MTGO's documented floor is .3300, but observed
  // ordering/display deltas line up with a one-third contribution.
  private const float MinimumWinPercentage = 1f / 3f;
  private const float PercentageTolerance = 0.0005f;
  private static readonly string[] s_roundResultBatchPaths =
  [
    "LoginID",
    "Rank",
    "Points",
    "OpponentMatchWinPercentage",
    "GameWinPercentage",
    "OpponentGameWinPercentage",
    "OpponentResults[].Round",
    "OpponentResults[].LoginID",
    "OpponentResults[].Win",
    "OpponentResults[].Loss",
    "OpponentResults[].Draw",
    "OpponentResults[].Bye"
  ];

  private ulong m_lastFingerprint;
  private ulong m_lastRankMismatchFingerprint;
  private IList<StandingRecord>? m_lastStandings;

  internal IList<StandingRecord> ComputeStandings(Tournament tournament)
  {
    int tournamentId = Try(() => tournament.Id, fallback: -1);
    bool hasPlayoffs = tournament.HasPlayoffs;
    int totalRounds = tournament.TotalRounds;

    var standings = tournament.Standings;

    var rounds = (IList<TournamentRound>)Try(
      () => tournament.Rounds,
      () => new List<TournamentRound>());

    var snapshot = BuildSnapshot(
      standings,
      rounds,
      hasPlayoffs,
      totalRounds);

    ulong inputFingerprint = ComputeFingerprint(
      snapshot,
      hasPlayoffs,
      totalRounds);

    bool cacheableSnapshot = IsCacheableSnapshot(snapshot);
    bool cacheHit =
      cacheableSnapshot &&
      m_lastStandings != null &&
      inputFingerprint == m_lastFingerprint;
    if (cacheHit)
    {
      return m_lastStandings;
    }

    var displayStats = ComputeStats(snapshot, maxRound: null);
    var swissStats = hasPlayoffs
      ? ComputeStats(snapshot, totalRounds)
      : displayStats;
    var tiebreakerStats = hasPlayoffs ? swissStats : displayStats;

    var swissSeedOrder = swissStats.Select(s => s.Id).ToList();
    IReadOnlyList<int> finalOrder = hasPlayoffs
      ? RankPlayerIdsForPlayoff(
          swissSeedOrder,
          GetCompletedPlayoffMatches(snapshot.Matches, totalRounds))
      : swissSeedOrder;

    var computedStandings = MaterializeStandings(
      displayStats,
      tiebreakerStats,
      finalOrder);

    if (cacheableSnapshot)
    {
      m_lastFingerprint = inputFingerprint;
      m_lastStandings = computedStandings;
    }

    LogRankMismatches(
      tournamentId,
      inputFingerprint,
      snapshot,
      tiebreakerStats,
      computedStandings);

    return computedStandings;
  }

  private static StandingSnapshot BuildSnapshot(
    IList<StandingRecord> standings,
    IList<TournamentRound> rounds,
    bool hasPlayoffs,
    int totalRounds)
  {
    var wrappersById = standings.ToDictionary(
      standing => (int)Unbind(standing).User.Id,
      standing => standing);

    var roundResults = ReadRoundResults(rounds);

    int latestResultRound = GetLatestResultRound(roundResults);

    int previousMatchCutoff = hasPlayoffs && latestResultRound >= totalRounds
      ? totalRounds
      : latestResultRound;

    var matches = ReadRoundMatches(rounds, previousMatchCutoff);

    return new(wrappersById, roundResults, matches, previousMatchCutoff == 0);
  }

  private static int GetLatestResultRound(IEnumerable<RoundResultInfo> roundResults)
  {
    int latestRound = 0;
    foreach (var result in roundResults)
    {
      latestRound = Math.Max(latestRound, result.Round);
      foreach (var opponent in result.Opponents)
      {
        latestRound = Math.Max(latestRound, opponent.Round);
      }
    }

    return latestRound;
  }

  private static List<MatchInfo> ReadRoundMatches(
    IEnumerable<TournamentRound> rounds,
    int minRoundExclusive)
  {
    var matches = new Dictionary<int, MatchInfo>();

    foreach (var round in rounds.OrderBy(round => round.Number))
    {
      int roundNumber = round.Number;
      if (roundNumber <= minRoundExclusive) continue;

      foreach (var user in round.UsersWithByes)
      {
        int playerId = user.Id;
        if (playerId <= 0) continue;

        int key = HashCode.Combine(roundNumber, playerId, true);
        if (!matches.ContainsKey(key))
        {
          matches.Add(key, new(
            -1,
            roundNumber,
            true,
            true,
            false,
            [playerId],
            [playerId],
            [],
            []));
        }
      }

      foreach (var match in round.Matches)
      {
        bool completed = match.IsComplete;
        if (!completed) continue;

        const bool hasBye = false;
        int id = match.Id;
        int key = id > 0 ? id : HashCode.Combine(roundNumber, matches.Count);
        if (matches.ContainsKey(key)) continue;

        var winners = match.WinningPlayers
          .Select(player => player.Id)
          .Where(playerId => playerId > 0)
          .Distinct()
          .ToArray();
        var losers = match.LosingPlayers
          .Select(player => player.Id)
          .Where(playerId => playerId > 0)
          .Distinct()
          .ToArray();
        // A completed match with no final places can be a real nullified/drawn
        // match. Preserve that source shape so normalization does not erase it.
        bool hadNoMatchResult = winners.Length == 0 && losers.Length == 0;

        var games = new List<GameInfo>();
        var gamePlayers = new List<int>();
        bool needGamePlayers = winners.Concat(losers).Distinct().Count() < 2;
        foreach (var game in match.Games)
        {
          if (needGamePlayers)
          {
            gamePlayers.AddRange(game.Players
              .Select(player => player.UserId)
              .Where(playerId => playerId > 0));
          }

          games.Add(new(
            game.Id,
            game.Status,
            game.WinningPlayers
              .Select(player => player.UserId)
              .Where(playerId => playerId > 0)
              .Distinct()
              .ToArray()));
        }

        var players = winners
          .Concat(losers)
          .Concat(gamePlayers)
          .Where(playerId => playerId > 0)
          .Distinct()
          .ToArray();
        NormalizeMatchResult(players, ref winners, ref losers, games);
        bool isDraw =
          completed &&
          hadNoMatchResult &&
          winners.Length == 0 &&
          losers.Length == 0;
        if (players.Length == 0)
        {
          players = winners.Concat(losers).Distinct().ToArray();
        }

        matches.Add(key, new(
          id,
          roundNumber,
          hasBye,
          completed,
          isDraw,
          players,
          winners,
          losers,
          games));
      }
    }

    return matches.Values
      .OrderBy(match => match.Round)
      .ThenBy(match => match.Id)
      .ToList();
  }

  private static void NormalizeMatchResult(
    int[] players,
    ref int[] winners,
    ref int[] losers,
    IReadOnlyCollection<GameInfo> games)
  {
    if (players.Length != 2) return;

    var winnerSet = winners.ToHashSet();
    var loserSet = losers.ToHashSet();
    if (winnerSet.Overlaps(loserSet))
    {
      // MTGO's MatchStandingRecord appends winner/loser ids on update, while
      // the underlying match object clears and repopulates them from final
      // results. If both lists overlap, trust completed game rows over stale
      // accumulated ids.
      if (TryResolveMatchResultFromGames(players, games, out winners, out losers))
      {
        return;
      }

      winners = [];
      losers = [];
      return;
    }

    if (winners.Length > 0 && losers.Length == 0)
    {
      losers = players.Where(playerId => !winnerSet.Contains(playerId)).ToArray();
    }
    else if (losers.Length > 0 && winners.Length == 0)
    {
      winners = players.Where(playerId => !loserSet.Contains(playerId)).ToArray();
    }
    else if (winners.Length == 0 && losers.Length == 0)
    {
      // Empty final-place lists are ambiguous: they can be a real draw/nullified
      // match or an incomplete live row. Only infer a winner when games prove it.
      TryResolveMatchResultFromGames(players, games, out winners, out losers);
    }
  }

  private static bool TryResolveMatchResultFromGames(
    int[] players,
    IReadOnlyCollection<GameInfo> games,
    out int[] winners,
    out int[] losers)
  {
    winners = [];
    losers = [];

    if (players.Length != 2) return false;

    int firstWins = 0;
    int secondWins = 0;
    foreach (var game in games)
    {
      if (game.Winners.Contains(players[0])) firstWins++;
      if (game.Winners.Contains(players[1])) secondWins++;
    }

    if (firstWins == secondWins) return false;

    winners = firstWins > secondWins ? [players[0]] : [players[1]];
    losers = firstWins > secondWins ? [players[1]] : [players[0]];
    return true;
  }

  private static List<RoundResultInfo> ReadRoundResults(
    IEnumerable<TournamentRound> rounds)
  {
    var results = new List<RoundResultInfo>();

    foreach (var round in rounds.OrderBy(round => round.Number))
    {
      int roundNumber = round.Number;
      if (TryReadRoundResultsBatch(
        round,
        roundNumber,
        results))
      {
        continue;
      }

      foreach (var playerResult in round.Results)
      {
        dynamic result = Unbind(playerResult);
        int playerId = result.LoginID;
        if (playerId <= 0)
        {
          continue;
        }

        int rank = result.Rank;
        int points = result.Points;
        string opponentMatchWinPercentage = result.OpponentMatchWinPercentage;
        string gameWinPercentage = result.GameWinPercentage;
        string opponentGameWinPercentage = result.OpponentGameWinPercentage;

        var opponents = new List<ServerOpponentResult>();
        foreach (var opponentResult in Map<dynamic>(result.OpponentResults))
        {
          dynamic opponent = Unbind(opponentResult);
          opponents.Add(new(
            opponent.Round,
            opponent.LoginID,
            opponent.Win,
            opponent.Loss,
            opponent.Draw,
            opponent.Bye));
        }

        results.Add(new(
          roundNumber,
          playerId,
          rank,
          points,
          ParsePercentage(opponentMatchWinPercentage),
          ParsePercentage(gameWinPercentage),
          ParsePercentage(opponentGameWinPercentage),
          opponents));
      }
    }

    return results;
  }

  private static bool TryReadRoundResultsBatch(
    TournamentRound round,
    int roundNumber,
    List<RoundResultInfo> results)
  {
    object? roundResults = Try(() => Unbind(round).Results, fallback: null);
    bool fetched = RemoteBatchCollection.TryFetch(
      roundResults,
      s_roundResultBatchPaths,
      out var batch);
    if (!fetched)
    {
      return false;
    }

    if (!batch.HasColumns(s_roundResultBatchPaths))
    {
      return false;
    }

    if (!batch.ColumnHasAnyValue("OpponentResults[].Round"))
    {
      return false;
    }

    for (int row = 0; row < batch.Count; row++)
    {
      int playerId = batch.GetInt(row, "LoginID");
      if (playerId <= 0)
      {
        continue;
      }

      int rank = batch.GetInt(row, "Rank");
      int points = batch.GetInt(row, "Points");
      string opponentMatchWinPercentage =
        batch.GetString(row, "OpponentMatchWinPercentage");
      string gameWinPercentage =
        batch.GetString(row, "GameWinPercentage");
      string opponentGameWinPercentage =
        batch.GetString(row, "OpponentGameWinPercentage");

      var opponents = ReadBatchOpponents(batch, row);

      results.Add(new(
        roundNumber,
        playerId,
        rank,
        points,
        ParsePercentage(opponentMatchWinPercentage),
        ParsePercentage(gameWinPercentage),
        ParsePercentage(opponentGameWinPercentage),
        opponents));
    }

    return true;
  }

  private static List<ServerOpponentResult> ReadBatchOpponents(
    BatchCollectionSnapshot batch,
    int row)
  {
    var opponentRounds = batch.GetIntArray(row, "OpponentResults[].Round");
    var opponentIds = batch.GetIntArray(row, "OpponentResults[].LoginID");
    var wins = batch.GetIntArray(row, "OpponentResults[].Win");
    var losses = batch.GetIntArray(row, "OpponentResults[].Loss");
    var draws = batch.GetIntArray(row, "OpponentResults[].Draw");
    var byes = batch.GetIntArray(row, "OpponentResults[].Bye");

    int count = new[]
    {
      opponentRounds.Count,
      opponentIds.Count,
      wins.Count,
      losses.Count,
      draws.Count,
      byes.Count
    }.Max();

    var opponents = new List<ServerOpponentResult>(count);
    for (int i = 0; i < count; i++)
    {
      opponents.Add(new(
        BatchCollectionSnapshot.GetAt(opponentRounds, i),
        BatchCollectionSnapshot.GetAt(opponentIds, i),
        BatchCollectionSnapshot.GetAt(wins, i),
        BatchCollectionSnapshot.GetAt(losses, i),
        BatchCollectionSnapshot.GetAt(draws, i),
        BatchCollectionSnapshot.GetAt(byes, i)));
    }

    return opponents;
  }

  private static IList<PlayerStanding> ComputeStats(
    StandingSnapshot snapshot,
    int? maxRound)
  {
    var wrappersById = snapshot.WrappersById;
    var players = wrappersById.ToDictionary(
      kvp => kvp.Key,
      kvp => new PlayerStanding(kvp.Key, kvp.Value));
    var serverResults = ReadServerResults(snapshot.RoundResults, maxRound);

    var serverMatches = ReadServerMatchResults(
      snapshot.RoundResults,
      maxRound,
      out int latestServerResultRound);
    foreach (var match in serverMatches)
    {
      GetStanding(players, wrappersById, match.PlayerId)
        .AddServerMatch(match);
    }
    foreach (var result in serverResults.Values)
    {
      GetStanding(players, wrappersById, result.PlayerId)
        .SetServerMatchWinBaseline(
          result.MatchWinPoints,
          result.MatchCount,
          result.Points,
          result.Byes);
    }

    foreach (var match in snapshot.Matches)
    {
      if (maxRound is int limit && match.Round > limit) continue;
      // Completed/resulted rounds use TournamentRound.Results/OpponentResults
      // as the authoritative match and bye baseline. PreviousMatches only
      // supplies live deltas after the latest server-result round.
      if (match.Round <= latestServerResultRound) continue;

      foreach (int playerId in match.Players)
      {
        GetStanding(players, wrappersById, playerId).AddMatch(match, playerId);
      }
    }

    var gameWinPercentages = ComputeGameWinPercentages(
      snapshot,
      maxRound,
      includeByes: true,
      enforceMinimum: false,
      validateAgainstServer: true);
    var opponentGameWinPercentages = ComputeGameWinPercentages(
      snapshot,
      maxRound,
      includeByes: false,
      enforceMinimum: true,
      validateAgainstServer: false);
    foreach (var standing in players.Values)
    {
      standing.FinalizeStats(
        gameWinPercentages.TryGetValue(standing.Id, out float gwp)
          ? gwp
          : 0f,
        opponentGameWinPercentages.TryGetValue(standing.Id, out float ogwp)
          ? ogwp
          : MinimumWinPercentage);
    }

    foreach (var standing in players.Values)
    {
      var opponents = standing.Opponents
        .Select(id => GetStanding(players, wrappersById, id))
        .ToList();
      if (opponents.Count == 0)
      {
        standing.OpponentMatchWinPercentage = MinimumWinPercentage;
        standing.OpponentGameWinPercentage = MinimumWinPercentage;
        continue;
      }

      standing.OpponentMatchWinPercentage =
        opponents.Average(opponent => opponent.MatchWinPercentage);
      standing.OpponentGameWinPercentage =
        opponents.Average(opponent => opponent.GameWinPercentageForOpponents);
    }

    return players.Values
      .Where(standing => standing.Wrapper != null)
      .OrderByDescending(standing => standing.Points)
      .ThenByDescending(standing =>
        PercentSortKey(standing.OpponentMatchWinPercentage))
      .ThenByDescending(standing =>
        PercentSortKey(standing.GameWinPercentage))
      .ThenByDescending(standing =>
        PercentSortKey(standing.OpponentGameWinPercentage))
      .ToList();
  }

  private static PlayerStanding GetStanding(
    IDictionary<int, PlayerStanding> players,
    IReadOnlyDictionary<int, StandingRecord> wrappersById,
    int playerId)
  {
    if (!players.TryGetValue(playerId, out var standing))
    {
      wrappersById.TryGetValue(playerId, out var wrapper);
      standing = new(playerId, wrapper);
      players[playerId] = standing;
    }

    return standing;
  }

  private static IList<ServerMatchResult> ReadServerMatchResults(
    IEnumerable<RoundResultInfo> roundResults,
    int? maxRound,
    out int latestResultRound)
  {
    var latestByPlayer = new Dictionary<int, List<ServerOpponentResult>>();
    int latestRound = 0;

    foreach (var result in roundResults.OrderBy(result => result.Round))
    {
      var opponentResults = new List<ServerOpponentResult>();
      foreach (var opponent in result.Opponents)
      {
        if (maxRound is int roundLimit && opponent.Round > roundLimit) continue;

        opponentResults.Add(opponent);
        latestRound = Math.Max(latestRound, opponent.Round);
      }

      latestByPlayer[result.PlayerId] = opponentResults;
    }

    latestResultRound = latestRound;

    var matches = new Dictionary<(int Round, int PlayerId, int OpponentId),
      ServerMatchResult>();
    foreach (var entry in latestByPlayer)
    {
      foreach (var result in entry.Value)
      {
        AddServerMatch(matches, new(
          result.Round,
          entry.Key,
          result.OpponentId,
          result.Wins,
          result.Losses,
          result.Draws,
          result.Byes,
          IsInferred: false));

        if (result.OpponentId <= 0 || result.Byes > 0)
        {
          continue;
        }

        AddServerMatch(matches, new(
          result.Round,
          result.OpponentId,
          entry.Key,
          result.Losses,
          result.Wins,
          result.Draws,
          Byes: 0,
          IsInferred: true));
      }
    }

    return matches.Values
      .OrderBy(match => match.Round)
      .ThenBy(match => match.PlayerId)
      .ThenBy(match => match.OpponentId)
      .ToList();
  }

  private static void AddServerMatch(
    IDictionary<(int Round, int PlayerId, int OpponentId), ServerMatchResult> matches,
    ServerMatchResult match)
  {
    var key = (match.Round, match.PlayerId, match.OpponentId);
    if (!matches.TryGetValue(key, out var existing) ||
        (existing.IsInferred && !match.IsInferred))
    {
      matches[key] = match;
    }
  }

  private static Dictionary<int, float> ComputeGameWinPercentages(
    StandingSnapshot snapshot,
    int? maxRound,
    bool includeByes,
    bool enforceMinimum,
    bool validateAgainstServer)
  {
    var playerIds = snapshot.WrappersById.Keys
      .Concat(snapshot.Matches.SelectMany(match => match.Players))
      .ToHashSet();
    var trustedGameHistory = playerIds.ToDictionary(id => id, _ => true);
    var serverGwpPlayers = new HashSet<int>();
    var latestServerGwp = playerIds.ToDictionary(
      id => id,
      _ => MinimumWinPercentage);

    foreach (var result in snapshot.RoundResults.OrderBy(result => result.Round))
    {
      if (maxRound is int limit && result.Round > limit) continue;

      int playerId = result.PlayerId;
      playerIds.Add(playerId);
      trustedGameHistory.TryAdd(playerId, true);
      latestServerGwp.TryAdd(playerId, MinimumWinPercentage);

      float serverGwp = result.GameWinPercentage;
      serverGwpPlayers.Add(playerId);
      if (validateAgainstServer && snapshot.HasCompletePreviousMatchHistory)
      {
        var counts = CountGames(playerId, snapshot.Matches, result.Round, includeByes);
        float reconstructedGwp = ComputeWinPercentage(
          counts.Wins,
          counts.Losses,
          counts.Draws,
          enforceMinimum);
        if (Math.Abs(reconstructedGwp - serverGwp) > PercentageTolerance)
        {
          trustedGameHistory[playerId] = false;
        }
      }

      latestServerGwp[playerId] = serverGwp;
    }

    var percentages = new Dictionary<int, float>();
    foreach (int playerId in playerIds)
    {
      if (!snapshot.HasCompletePreviousMatchHistory &&
          serverGwpPlayers.Contains(playerId) &&
          latestServerGwp.TryGetValue(playerId, out float serverGwp))
      {
        // Once completed rounds are backed by TournamentRound.Results, we no
        // longer have the full game-count denominator locally. Use MTGO's
        // cumulative GWP scalar for that baseline instead of mixing it with a
        // partial PreviousMatches history.
        percentages[playerId] = enforceMinimum
          ? Math.Max(MinimumWinPercentage, serverGwp)
          : serverGwp;
        continue;
      }

      if (validateAgainstServer && !trustedGameHistory[playerId])
      {
        percentages[playerId] = latestServerGwp[playerId];
        continue;
      }

      var counts = CountGames(playerId, snapshot.Matches, maxRound, includeByes);
      percentages[playerId] = ComputeWinPercentage(
        counts.Wins,
        counts.Losses,
        counts.Draws,
        enforceMinimum);
    }

    return percentages;
  }

  private static GameCounts CountGames(
    int playerId,
    IEnumerable<MatchInfo> matches,
    int? maxRound,
    bool includeByes)
  {
    int wins = 0;
    int losses = 0;
    int draws = 0;

    foreach (var match in matches)
    {
      if (maxRound is int limit && match.Round > limit) continue;
      if (!match.Players.Contains(playerId))
        continue;

      if (match.HasBye)
      {
        if (includeByes) wins += 2;
        continue;
      }

      if (!match.Completed)
        continue;

      bool wonMatch = match.Winners.Contains(playerId);
      bool lostMatch = match.Losers.Contains(playerId);
      int matchWins = 0;
      int matchLosses = 0;
      int matchDraws = 0;

      foreach (var game in match.Games)
      {
        if (game.Winners.Length > 0)
        {
          if (game.Winners.Contains(playerId)) matchWins++;
          else matchLosses++;
        }
        else if (game.Status == GameStatus.Finished)
        {
          matchDraws++;
        }
      }

      if (!((wonMatch && matchWins >= matchLosses) ||
            (lostMatch && matchLosses >= matchWins) ||
            (!wonMatch && !lostMatch)))
      {
        // Do not invent a missing game from the match winner. GWP/OGWP are game
        // tiebreakers, so they must come from actual GameStandingRecords; the
        // server GWP fallback handles historical rows that do not reconstruct.
        Log.Debug(
          "[StandingTable] Unexpected game/match result for player {Id} " +
          "in match {MatchId}: wonMatch={Won}, lostMatch={Lost}, " +
          "gameWins={GW}, gameLosses={GL}",
          playerId,
          match.Id,
          wonMatch,
          lostMatch,
          matchWins,
          matchLosses);
      }

      wins += matchWins;
      losses += matchLosses;
      draws += matchDraws;
    }

    return new(wins, losses, draws);
  }

  private static IList<PlayoffMatch> GetCompletedPlayoffMatches(
    IEnumerable<MatchInfo> matches,
    int swissRounds)
  {
    return matches
      .Where(match =>
        !match.HasBye &&
        match.Completed &&
        match.Round > swissRounds &&
        match.Winners.Length > 0 &&
        match.Losers.Length > 0)
      .Select(match => new PlayoffMatch(match.Round, match.Winners, match.Losers))
      .OrderBy(match => match.Round)
      .ToList();
  }

  private static int[] RankPlayerIdsForPlayoff(
    IReadOnlyList<int> swissSeedOrder,
    IList<PlayoffMatch> playoffMatches)
  {
    if (playoffMatches.Count == 0) return swissSeedOrder.ToArray();

    var seedIndex = swissSeedOrder
      .Select((id, index) => new { id, index })
      .ToDictionary(x => x.id, x => x.index);
    var playoffPlayers = swissSeedOrder.Take(PlayoffCut).ToHashSet();
    var eliminatedRoundByPlayer = new Dictionary<int, int>();

    foreach (var match in playoffMatches)
    {
      foreach (int loserId in match.Losers)
      {
        if (playoffPlayers.Contains(loserId))
        {
          eliminatedRoundByPlayer.TryAdd(loserId, match.Round);
        }
      }
    }

    int lastCompletedPlayoffRound = playoffMatches.Max(match => match.Round);
    int SortBand(int playerId)
    {
      if (!playoffPlayers.Contains(playerId)) return 100;
      if (!eliminatedRoundByPlayer.TryGetValue(playerId, out int round)) return 0;

      return 1 + lastCompletedPlayoffRound - round;
    }

    return swissSeedOrder
      .OrderBy(SortBand)
      .ThenBy(id => seedIndex[id])
      .ToArray();
  }

  private static IList<StandingRecord> MaterializeStandings(
    IList<PlayerStanding> stats,
    IList<PlayerStanding> tiebreakerStats,
    IReadOnlyList<int> finalOrder)
  {
    var statsById = stats.ToDictionary(stat => stat.Id);
    var tiebreakerStatsById = tiebreakerStats.ToDictionary(stat => stat.Id);
    var orderedIds = finalOrder
      .Where(statsById.ContainsKey)
      .Concat(stats.Select(stat => stat.Id).Where(id => !finalOrder.Contains(id)))
      .ToList();

    int rank = 1;
    return orderedIds.Select(id =>
    {
      var stat = statsById[id];
      if (!tiebreakerStatsById.TryGetValue(id, out var tiebreakers))
      {
        tiebreakers = stat;
      }

      return new StandingRecord(new
      {
        Rank = rank++,
        Points = stat.Points,
        Record = stat.Record,
        OpponentMatchWinPercentage = tiebreakers.OpponentMatchWinPercentage.ToString("P"),
        GameWinPercentage = tiebreakers.GameWinPercentage.ToString("P"),
        OpponentGameWinPercentage = tiebreakers.OpponentGameWinPercentage.ToString("P"),
        User = Unbind(stat.Wrapper!).User,
        PreviousMatches = Unbind(stat.Wrapper!).PreviousMatches
      });
    }).ToList();
  }

  private void LogRankMismatches(
    int tournamentId,
    ulong inputFingerprint,
    StandingSnapshot snapshot,
    IList<PlayerStanding> tiebreakerStats,
    IList<StandingRecord> computedStandings)
  {
    if (m_lastRankMismatchFingerprint == inputFingerprint) return;
    m_lastRankMismatchFingerprint = inputFingerprint;

    var serverResults = ReadServerResults(snapshot.RoundResults);
    if (serverResults.Count == 0) return;

    var statsById = tiebreakerStats.ToDictionary(stat => stat.Id);
    var computedRanks = computedStandings
      .Select((standing, index) => new
      {
        Id = (int)Unbind(standing).User.Id,
        Rank = index + 1
      })
      .ToDictionary(item => item.Id, item => item.Rank);

    foreach (var serverResult in serverResults.Values.OrderBy(result => result.Rank))
    {
      if (!statsById.TryGetValue(serverResult.PlayerId, out var stat) ||
          !computedRanks.TryGetValue(serverResult.PlayerId, out int computedRank) ||
          stat.Points != serverResult.Points ||
          computedRank == serverResult.Rank)
      {
        continue;
      }

      Log.Warning(
        "[StandingTable] rank mismatch tournament={TournamentId} " +
        "round={Round} player={Player}({PlayerId}) " +
        "computedRank={ComputedRank} serverRank={ServerRank} " +
        "points={Points} computedOMW={ComputedOMW} serverOMW={ServerOMW} " +
        "computedGW={ComputedGW} serverGW={ServerGW} " +
        "computedOGW={ComputedOGW} serverOGW={ServerOGW} " +
        "omwContributions=[{OMWContributions}]",
        tournamentId,
        serverResult.Round,
        FormatPlayerLabel(stat),
        stat.Id,
        computedRank,
        serverResult.Rank,
        stat.Points,
        FormatPercentage(stat.OpponentMatchWinPercentage),
        FormatPercentage(serverResult.OpponentMatchWinPercentage),
        FormatPercentage(stat.GameWinPercentage),
        FormatPercentage(serverResult.GameWinPercentage),
        FormatPercentage(stat.OpponentGameWinPercentage),
        FormatPercentage(serverResult.OpponentGameWinPercentage),
        FormatOmwContributions(stat, statsById));
    }
  }

  private static Dictionary<int, ServerStandingResult> ReadServerResults(
    IEnumerable<RoundResultInfo> roundResults,
    int? maxRound = null)
  {
    var results = new Dictionary<int, ServerStandingResult>();

    foreach (var result in roundResults.OrderBy(result => result.Round))
    {
      if (maxRound is int limit && result.Round > limit) continue;

      int byes = 0;
      int matchCount = 0;
      foreach (var opponent in result.Opponents)
      {
        if (maxRound is int resultLimit && opponent.Round > resultLimit) continue;

        if (opponent.Byes > 0)
        {
          byes += opponent.Byes;
          continue;
        }

        // OpponentResults identify opponents, but their W/L/D fields are not
        // safe as per-match deltas here. The authoritative points come from
        // the server standing snapshot; each non-bye row only contributes to
        // the match denominator.
        matchCount++;
      }

      int points = result.Points;
      int matchPoints = Math.Max(0, result.Points - (byes * 3));

      results[result.PlayerId] = new(
        result.PlayerId,
        result.Round,
        result.Rank,
        points,
        matchPoints,
        matchCount,
        byes,
        result.OpponentMatchWinPercentage,
        result.GameWinPercentage,
        result.OpponentGameWinPercentage);
    }

    return results;
  }

  private static string FormatOmwContributions(
    PlayerStanding standing,
    IReadOnlyDictionary<int, PlayerStanding> statsById)
  {
    return string.Join(", ", standing.Opponents.Select(opponentId =>
      statsById.TryGetValue(opponentId, out var opponent)
        ? $"{FormatPlayerLabel(opponent)}({opponentId})={FormatPercentage(opponent.MatchWinPercentage)}"
        : $"{opponentId}=missing"));
  }

  private static string FormatPlayerLabel(PlayerStanding standing) =>
    standing.Wrapper != null
      ? (string)Unbind(standing.Wrapper).User.Name
      : standing.Id.ToString(CultureInfo.InvariantCulture);

  private static string FormatPercentage(float value) =>
    value.ToString("0.0000", CultureInfo.InvariantCulture);

  private static bool IsCacheableSnapshot(StandingSnapshot snapshot)
  {
    foreach (var match in snapshot.Matches)
    {
      if (match.HasBye ||
          match.Completed ||
          match.Winners.Length > 0 ||
          match.Losers.Length > 0 ||
          match.Games.Any(game =>
            game.Status == GameStatus.Finished ||
            game.Winners.Length > 0))
      {
        return true;
      }
    }

    foreach (var result in snapshot.RoundResults)
    {
      if (result.Points != 0)
      {
        return true;
      }

      foreach (var opponent in result.Opponents)
      {
        if (opponent.Wins != 0 ||
            opponent.Losses != 0 ||
            opponent.Draws != 0 ||
            opponent.Byes != 0)
        {
          return true;
        }
      }
    }

    // A tournament viewed before its first result can expose fully shaped but
    // all-zero standings, sometimes with ranks/default percentages already
    // populated. Recompute those provisional snapshots so they cannot poison
    // the cache once real results arrive.
    return false;
  }

  private static ulong ComputeFingerprint(
    StandingSnapshot snapshot,
    bool hasPlayoffs,
    int swissRounds)
  {
    ulong hash = 14695981039346656037UL;
    AddFingerprint(ref hash, hasPlayoffs);
    AddFingerprint(ref hash, swissRounds);

    foreach (int playerId in snapshot.WrappersById.Keys.Order())
    {
      AddFingerprint(ref hash, playerId);
    }

    foreach (var result in snapshot.RoundResults
      .OrderBy(result => result.Round)
      .ThenBy(result => result.PlayerId))
    {
      AddFingerprint(ref hash, result.Round);
      AddFingerprint(ref hash, result.PlayerId);
      AddFingerprint(ref hash, result.Rank);
      AddFingerprint(ref hash, result.Points);
      AddFingerprint(ref hash, PercentSortKey(result.OpponentMatchWinPercentage));
      AddFingerprint(ref hash, PercentSortKey(result.GameWinPercentage));
      AddFingerprint(ref hash, PercentSortKey(result.OpponentGameWinPercentage));

      foreach (var opponent in result.Opponents
        .OrderBy(opponent => opponent.Round)
        .ThenBy(opponent => opponent.OpponentId)
        .ThenBy(opponent => opponent.Wins)
        .ThenBy(opponent => opponent.Losses)
        .ThenBy(opponent => opponent.Draws)
        .ThenBy(opponent => opponent.Byes))
      {
        AddFingerprint(ref hash, opponent.Round);
        AddFingerprint(ref hash, opponent.OpponentId);
        AddFingerprint(ref hash, opponent.Wins);
        AddFingerprint(ref hash, opponent.Losses);
        AddFingerprint(ref hash, opponent.Draws);
        AddFingerprint(ref hash, opponent.Byes);
      }
    }

    foreach (var match in snapshot.Matches
      .OrderBy(match => match.Round)
      .ThenBy(match => match.Id))
    {
      AddFingerprint(ref hash, match.Id);
      AddFingerprint(ref hash, match.Round);
      AddFingerprint(ref hash, match.HasBye);
      AddFingerprint(ref hash, match.Completed);
      AddFingerprint(ref hash, match.IsDraw);
      foreach (int playerId in match.Players.Order())
        AddFingerprint(ref hash, playerId);
      foreach (int playerId in match.Winners.Order())
        AddFingerprint(ref hash, playerId);
      foreach (int playerId in match.Losers.Order())
        AddFingerprint(ref hash, playerId);
      foreach (var game in match.Games.OrderBy(game => game.Id))
      {
        AddFingerprint(ref hash, game.Id);
        AddFingerprint(ref hash, game.Status.GetHashCode());
        foreach (int playerId in game.Winners.Order())
          AddFingerprint(ref hash, playerId);
      }
    }

    return hash;
  }

  private static void AddFingerprint(ref ulong hash, bool value) =>
    AddFingerprint(ref hash, value ? 1 : 0);

  private static void AddFingerprint(ref ulong hash, int value)
  {
    unchecked
    {
      hash ^= (uint)value;
      hash *= 1099511628211UL;
    }
  }

  private static float ComputeWinPercentage(
    int wins,
    int losses,
    int draws,
    bool enforceMinimum = true)
  {
    int played = wins + losses + draws;
    float percentage = played == 0
      ? 0f
      : (float)(wins * 3 + draws) / (played * 3);
    return enforceMinimum
      ? Math.Max(MinimumWinPercentage, percentage)
      : percentage;
  }

  private static float ComputeWinPercentageFromPoints(
    int points,
    int played,
    bool enforceMinimum = true)
  {
    float percentage = played == 0
      ? 0f
      : (float)points / (played * 3);
    return enforceMinimum
      ? Math.Max(MinimumWinPercentage, percentage)
      : percentage;
  }

  private static float ParsePercentage(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return MinimumWinPercentage;

    string normalized = value.Trim();
    if (normalized.EndsWith("%", StringComparison.Ordinal))
    {
      normalized = normalized[..^1];
    }

    return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent) ||
           float.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out percent)
      ? percent > 1 ? percent / 100f : percent
      : MinimumWinPercentage;
  }

  private static int PercentSortKey(float value) =>
    (int)Math.Round(value * 10000, MidpointRounding.AwayFromZero);

  private sealed class PlayerStanding(int id, StandingRecord? wrapper)
  {
    public int Id { get; } = id;
    public StandingRecord? Wrapper { get; } = wrapper;
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Draws { get; private set; }
    public int Points { get; private set; }
    public float MatchWinPercentage { get; private set; }
    public float GameWinPercentage { get; private set; }
    public float GameWinPercentageForOpponents { get; private set; }
    public float OpponentMatchWinPercentage { get; set; }
    public float OpponentGameWinPercentage { get; set; }
    public List<int> Opponents { get; } = [];
    public string Record
    {
      get
      {
        int wins = Wins + (m_serverWins ?? 0);
        int losses = Losses + (m_serverLosses ?? 0);
        int draws = Draws + (m_serverDraws ?? 0);
        return $"{wins}-{losses}-{draws}";
      }
    }

    private int m_matchWinsForOpponents;
    private int m_matchLossesForOpponents;
    private int m_matchDrawsForOpponents;
    private int? m_serverMatchWinPoints;
    private int? m_serverMatchCount;
    private int? m_serverPoints;
    private int? m_serverWins;
    private int? m_serverLosses;
    private int? m_serverDraws;
    private readonly HashSet<(int Round, int OpponentId)> m_opponentKeys = [];

    public void AddServerMatch(ServerMatchResult match)
    {
      for (int i = 0; i < match.Byes; i++)
      {
        AddMatch(match.Round, 0, 1, 0, 0, isBye: true);
      }

      if (match.OpponentId <= 0 || match.Byes > 0)
      {
        return;
      }

      AddOpponent(match.Round, match.OpponentId);
    }

    public void AddMatch(MatchInfo match, int playerId)
    {
      if (match.HasBye)
      {
        AddMatch(match.Round, 0, 1, 0, 0, isBye: true);
        return;
      }

      bool won = match.Winners.Contains(playerId);
      bool lost = match.Losers.Contains(playerId);
      if (!won && !lost && !match.IsDraw) return;

      // For live rows, only count a draw when the completed source row had no
      // final places and game rows could not resolve a winner.
      AddMatch(
        match.Round,
        match.Players.FirstOrDefault(id => id != playerId),
        won ? 1 : 0,
        lost ? 1 : 0,
        match.IsDraw ? 1 : 0);
    }

    public void FinalizeStats(
      float gameWinPercentage,
      float gameWinPercentageForOpponents)
    {
      if (m_serverMatchWinPoints is int serverPoints &&
          m_serverMatchCount is int serverMatches)
      {
        int livePoints = (m_matchWinsForOpponents * 3) +
          m_matchDrawsForOpponents;
        int liveMatches = m_matchWinsForOpponents +
          m_matchLossesForOpponents +
          m_matchDrawsForOpponents;
        MatchWinPercentage = ComputeWinPercentageFromPoints(
          serverPoints + livePoints,
          serverMatches + liveMatches);
        Points = (m_serverPoints ?? serverPoints) + livePoints;
      }
      else
      {
        Points = Wins * 3 + Draws;
        MatchWinPercentage = ComputeWinPercentage(
          m_matchWinsForOpponents,
          m_matchLossesForOpponents,
          m_matchDrawsForOpponents);
      }
      GameWinPercentage = gameWinPercentage;
      GameWinPercentageForOpponents = gameWinPercentageForOpponents;
    }

    public void SetServerMatchWinBaseline(
      int matchWinPoints,
      int matches,
      int points,
      int byes)
    {
      // Server result snapshots already contain the authoritative match-point
      // total. OpponentResults rows are still useful for opponent identity, but
      // their W/L/D fields are not safe as match-count deltas.
      m_serverMatchWinPoints = matchWinPoints;
      m_serverMatchCount = matches;
      m_serverPoints = points;
      (m_serverWins, m_serverLosses, m_serverDraws) =
        ComputeRecordFromPoints(matchWinPoints, matches, byes);
      Wins = 0;
      Losses = 0;
      Draws = 0;
      m_matchWinsForOpponents = 0;
      m_matchLossesForOpponents = 0;
      m_matchDrawsForOpponents = 0;
    }

    private static (int Wins, int Losses, int Draws) ComputeRecordFromPoints(
      int matchPoints,
      int matches,
      int byes)
    {
      int matchWins = Math.Min(matches, Math.Max(0, matchPoints / 3));
      int draws = Math.Min(
        Math.Max(0, matches - matchWins),
        Math.Max(0, matchPoints - (matchWins * 3)));
      int losses = Math.Max(0, matches - matchWins - draws);
      return (matchWins + byes, losses, draws);
    }

    private void AddMatch(
      int round,
      int opponentId,
      int wins,
      int losses,
      int draws,
      bool isBye = false)
    {
      Wins += wins;
      Losses += losses;
      Draws += draws;
      if (isBye) return;
      m_matchWinsForOpponents += wins;
      m_matchLossesForOpponents += losses;
      m_matchDrawsForOpponents += draws;
      if (opponentId > 0) AddOpponent(round, opponentId);
    }

    private void AddOpponent(int round, int opponentId)
    {
      if (opponentId > 0 && m_opponentKeys.Add((round, opponentId)))
      {
        Opponents.Add(opponentId);
      }
    }
  }

  private sealed record MatchInfo(
    int Id,
    int Round,
    bool HasBye,
    bool Completed,
    bool IsDraw,
    int[] Players,
    int[] Winners,
    int[] Losers,
    List<GameInfo> Games
  );

  private sealed record StandingSnapshot(
    IReadOnlyDictionary<int, StandingRecord> WrappersById,
    List<RoundResultInfo> RoundResults,
    List<MatchInfo> Matches,
    bool HasCompletePreviousMatchHistory
  );

  private sealed record RoundResultInfo(
    int Round,
    int PlayerId,
    int Rank,
    int Points,
    float OpponentMatchWinPercentage,
    float GameWinPercentage,
    float OpponentGameWinPercentage,
    List<ServerOpponentResult> Opponents
  );

  private readonly record struct GameInfo(
    int Id,
    GameStatus Status,
    int[] Winners
  );

  private readonly record struct PlayoffMatch(
    int Round,
    int[] Winners,
    int[] Losers
  );

  private readonly record struct ServerOpponentResult(
    int Round,
    int OpponentId,
    int Wins,
    int Losses,
    int Draws,
    int Byes
  );

  private readonly record struct ServerMatchResult(
    int Round,
    int PlayerId,
    int OpponentId,
    int Wins,
    int Losses,
    int Draws,
    int Byes,
    bool IsInferred
  );

  private readonly record struct ServerStandingResult(
    int PlayerId,
    int Round,
    int Rank,
    int Points,
    int MatchWinPoints,
    int MatchCount,
    int Byes,
    float OpponentMatchWinPercentage,
    float GameWinPercentage,
    float OpponentGameWinPercentage
  );

  private readonly record struct GameCounts(
    int Wins,
    int Losses,
    int Draws
  );
}
