/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Serialization;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Tournaments;

public sealed class TournamentRound(dynamic tournamentRound)
    : DLRWrapper<ITournamentRound>
{
  private static readonly string[] s_matchesInProgressPlayerNameBatchPaths =
  [
    "IsCompleted",
    "Status",
    "JoinedUsers[].Name"
  ];

  private static readonly string[] s_roundHashMatchBatchPaths =
  [
    "MatchId",
    "Status",
    "IsCompleted",
    "WinningPlayers[].Id",
    "LosingPlayers[].Id"
  ];

  /// <summary>
  /// Stores an internal reference to the ITournamentRound object.
  /// </summary>
  internal override dynamic obj => tournamentRound;

  private DynamicRemoteObject m_matches =>
    (DynamicRemoteObject)Unbind(this).Matches;

  private DynamicRemoteObject m_matchesInProgressCandidates =>
    m_matches.Filter<IPlayerEvent>(m => m.IsCompleted == false);

  /// <summary>
  /// The raw server result snapshots for this tournament round.
  /// </summary>
  [NonSerializable]
  internal IEnumerable<dynamic> Results => Map<dynamic>(Unbind(this).Results);

  //
  // ITournamentRound wrapper properties
  //

  /// <summary>
  /// The tournament round's number.
  /// </summary>
  public int Number => @base.Number;

  /// <summary>
  /// Whether the tournament round is complete.
  /// </summary>
  public bool IsComplete =>
    @base.IsComplete || MatchesInProgress.Any() == false;

  /// <summary>
  /// The tournament round's matches.
  /// </summary>
  public IEnumerable<Match> Matches => Map<Match>(@base.Matches);

  /// <summary>
  /// The tournament round's matches that are not completed.
  /// </summary>
  public IEnumerable<Match> MatchesInProgress =>
    Map<Match>(m_matchesInProgressCandidates)
      .Where(match => !match.IsComplete)
      .ToArray();

  /// <summary>
  /// Names of players whose matches in this round are still in progress.
  /// </summary>
  public IEnumerable<string> PlayerNamesWithMatchesInProgress =>
    TryReadPlayerNamesWithMatchesInProgressBatch(out var names)
      ? names
      : MatchesInProgress
        .SelectMany(match => match.Players)
        .Select(player => player.Name)
        .Where(name => !string.IsNullOrEmpty(name))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

  /// <summary>
  /// The start time of the tournament round.
  /// </summary>
  public DateTime StartTime => @base.StartTime;

  /// <summary>
  /// The users assigned a bye in the tournament round.
  /// </summary>
  public IEnumerable<User> UsersWithByes => Map<User>(@base.UsersWithByes);

  //
  // ITournamentRound wrapper methods
  //

  internal int[] GetRoundHashMatchHashes() =>
    TryReadRoundHashMatchHashesBatch(out int[] matchHashes)
      ? matchHashes
      : Matches
        .Where(match => match.IsComplete)
        .Select(GetCompletedMatchHash)
        .OrderBy(hash => hash)
        .ToArray();

  private bool TryReadRoundHashMatchHashesBatch(out int[] matchHashes)
  {
    matchHashes = [];

    if (!RemoteBatchCollection.TryFetch(
          m_matches,
          s_roundHashMatchBatchPaths,
          out var batch) ||
        !batch.HasColumns(s_roundHashMatchBatchPaths))
    {
      return false;
    }

    var hashes = new List<int>();
    for (int row = 0; row < batch.Count; row++)
    {
      bool isCompleted = IsCompletedValue(batch.GetString(row, "IsCompleted"));
      MatchState state = ReadMatchState(batch.GetString(row, "Status"));
      if (!isCompleted && !IsCompletedMatchState(state))
      {
        continue;
      }

      hashes.Add(GetCompletedMatchHash(
        batch.GetInt(row, "MatchId"),
        batch.GetIntArray(row, "WinningPlayers[].Id"),
        batch.GetIntArray(row, "LosingPlayers[].Id")));
    }

    matchHashes = hashes
      .OrderBy(hash => hash)
      .ToArray();
    return true;
  }

  private static int GetCompletedMatchHash(Match match) =>
    GetCompletedMatchHash(
      match.Id,
      match.WinningPlayers.Select(player => player.Id),
      match.LosingPlayers.Select(player => player.Id));

  private static int GetCompletedMatchHash(
    int matchId,
    IEnumerable<int> winnerIds,
    IEnumerable<int> loserIds)
  {
    var hc = new HashCode();
    hc.Add(matchId);
    foreach (int playerId in winnerIds
               .Where(playerId => playerId > 0)
               .OrderBy(playerId => playerId))
    {
      hc.Add(playerId);
    }

    hc.Add(0);
    foreach (int playerId in loserIds
               .Where(playerId => playerId > 0)
               .OrderBy(playerId => playerId))
    {
      hc.Add(playerId);
    }

    return hc.ToHashCode();
  }

  private bool TryReadPlayerNamesWithMatchesInProgressBatch(
    out string[] playerNames)
  {
    playerNames = [];

    if (!RemoteBatchCollection.TryFetch(
          m_matchesInProgressCandidates,
          s_matchesInProgressPlayerNameBatchPaths,
          out var batch) ||
        !batch.HasColumns(s_matchesInProgressPlayerNameBatchPaths))
    {
      return false;
    }

    var names = new HashSet<string>(StringComparer.Ordinal);
    for (int row = 0; row < batch.Count; row++)
    {
      if (IsCompletedValue(batch.GetString(row, "IsCompleted")) ||
          IsCompletedMatchStatus(batch.GetString(row, "Status")))
      {
        continue;
      }

      foreach (string name in batch.GetStringArray(row, "JoinedUsers[].Name"))
      {
        names.Add(name);
      }
    }

    playerNames = names
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();

    return true;
  }

  private static bool IsCompletedValue(string value) =>
    bool.TryParse(value, out bool completed) && completed;

  private static MatchState ReadMatchState(string status) =>
    Enum.TryParse(status, out MatchState state)
      ? state
      : MatchState.Invalid;

  private static bool IsCompletedMatchStatus(string status) =>
    IsCompletedMatchState(ReadMatchState(status));

  private static bool IsCompletedMatchState(MatchState state) =>
    state.HasFlag(MatchState.MatchCompleted) &&
    state.HasFlag(MatchState.GameClosed);

}
