/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Captures game and match outcomes at native callbacks before their source
/// objects can change or close, and converts them to detached SDK results.
/// </summary>
internal static class ResultProcessor
{
  internal static readonly EventHookProxy<Game, IList<GamePlayerResult>> GameResultsChanged =
    CreateResultsHook("HandleWinningPlayers", rankings: false);

  internal static readonly EventHookProxy<Game, IList<GamePlayerResult>> PlayerRankingsChanged =
    CreateResultsHook("HandlePlayerRanking", rankings: true);

  private static EventHookProxy<Game, IList<GamePlayerResult>> CreateResultsHook(
    string method, bool rankings)
  {
    var batch = new RemoteBatch();
    var gameInput = batch.Input(ObjectOrRemoteAddress.Null);
    var message = batch.Input(ObjectOrRemoteAddress.Null);
    AppendGameResultsCapture(batch, gameInput, message, rankings);

    return new("WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game", method,
      new CapturedEventHook((context, instance, _) =>
      {
        var values = (context.Capture
          ?? throw new InvalidOperationException("Game results capture is missing.")).Values;
        var results = ReadGameResults(values, rankings);
        return results.Count == 0 ? null : (new Game(instance), results);
      }),
      new RemoteHookCapture { Batch = batch.ToRequest(), Inputs = [-1, 0] },
      HarmonyPatchPosition.Postfix);
  }

  internal static IList<GamePlayerResult> ReadGameResults(Game game)
  {
    var batch = new RemoteBatch { ForceUIThread = true };
    var input = batch.Input(Unbind(game));
    AppendGameResultsCapture(batch, input, batch.Read(input, "PlayerRankings"), rankings: true);
    return ReadGameResults(batch.Execute(RemoteClient.@client).Values, rankings: true);
  }

  private static void AppendGameResultsCapture(
    RemoteBatch batch, RemoteBatchSlot gameInput, RemoteBatchSlot message, bool rankings)
  {
    var scalar = RemoteBatchProjection.Value();
    batch.Output("isReplay", batch.Read(gameInput, "IsReplay"));
    batch.Output("duration", batch.Read(gameInput, "CompletedDuration"));
    batch.Output("players", batch.Read(gameInput, "PlayersInServerOrder"),
      RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
        ("id", "User.Id", scalar), ("name", "User.Name", scalar))));
    if (rankings)
    {
      batch.Output("results", message,
        RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
          ("id", "ID", scalar), ("result", "Ranking", scalar))));
    }
    else
    {
      batch.Output("movedFirst", batch.Read(message, "MovedFirst"));
      batch.Output("results", batch.Read(message, "GameResults"),
        RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
          ("result", "Results", scalar))));
    }
  }

  private static IList<GamePlayerResult> ReadGameResults(
    Dictionary<string, RemoteBatchValue> values, bool rankings)
  {
    if (values["isReplay"].GetScalar<bool>()) return [];
    var players = values["players"].Items;
    var entries = values["results"].Items;
    if (entries.Count == 0) return [];
    // Native clock values can be unavailable. Do not substitute match duration.
    TimeSpan elapsed = values["duration"].GetScalar<TimeSpan>();
    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

    IList<GamePlayerResult> results = new List<GamePlayerResult>();
    for (int index = 0; index < entries.Count; index++)
    {
      var entry = entries[index].Members;
      int outcome = entry["result"].GetScalar<int>();
      if (outcome == 1) continue; // Still playing is not a result.
      var player = rankings
        ? players.First(p => p.Members["id"].GetScalar<int>() == entry["id"].GetScalar<int>())
        : players[index];
      PlayDrawResult playDraw = rankings ? PlayDrawResult.Unknown
        : index == values["movedFirst"].GetScalar<int>() ? PlayDrawResult.Play : PlayDrawResult.Draw;
      results.Add(new(player.Members["name"].GetScalar<string>(), playDraw,
        (GameResult)outcome, elapsed));
    }
    return results;
  }

  internal static IList<PlayerResult> ReadMatchResults(Match match)
  {
    var batch = new RemoteBatch { ForceUIThread = true };
    AppendMatchResultsCapture(batch, batch.Input(Unbind(match)));
    return ReadMatchResults(batch.Execute(RemoteClient.@client).Values);
  }

  /// <summary>
  /// Reports final outcomes and game scores captured before native callback delivery.
  /// The sender is the match ID, which remains usable after the match is removed.
  /// </summary>
  internal static event Action<int, IList<PlayerResult>> MatchResultsChanged
  {
    add
    {
      for (int index = 0; index < s_matchResultsHooks.Length; index++)
        s_matchResultsHooks[index] += value;
    }
    remove
    {
      for (int index = 0; index < s_matchResultsHooks.Length; index++)
        s_matchResultsHooks[index] -= value;
    }
  }

  private static readonly EventHookProxy<int, IList<PlayerResult>>[] s_matchResultsHooks =
  [
    CreateMatchResultsHook("MatchEvent.Match+MatchBaseState"),
    CreateMatchResultsHook("LeagueMatchEvent.LeagueMatch+LeagueMatchBaseState"),
    CreateMatchResultsHook("TournamentMatchEvent.TournamentMatch+TournamentMatchBaseState")
  ];

  /// <summary>
  /// Installs result captures for challenge, league, and tournament matches.
  /// </summary>
  internal static void EnsureMatchHooksInitialized()
  {
    foreach (var hook in s_matchResultsHooks) hook.EnsureInitialize();
  }

  private static EventHookProxy<int, IList<PlayerResult>> CreateMatchResultsHook(string stateType)
  {
    var batch = new RemoteBatch();
    var finalResults = batch.Input(ObjectOrRemoteAddress.Null);
    var matchInput = batch.Input(ObjectOrRemoteAddress.Null);
    AppendMatchResultsCapture(batch, matchInput);
    batch.Output("finalResults", finalResults,
      RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
        ("id", "LoginID", RemoteBatchProjection.Value()),
        ("result", "FinalPlace", RemoteBatchProjection.Value()))));

    return new("WotC.MtGO.Client.Model.Play." + stateType,
      "UpdateMatchResults", new CapturedEventHook((context, _, _) =>
      {
        var values = (context.Capture
          ?? throw new InvalidOperationException("Match results capture is missing.")).Values;
        if (values["finalResults"].Items.Count == 0) return null;
        return (values["id"].GetScalar<int>(), ReadMatchResults(values));
      }), new RemoteHookCapture
      {
        Batch = batch.ToRequest(), Inputs = [0, 1], IncludeParameters = false
      }, HarmonyPatchPosition.Postfix);
  }

  private static void AppendMatchResultsCapture(RemoteBatch batch, RemoteBatchSlot matchInput)
  {
    var scalar = RemoteBatchProjection.Value();
    var user = RemoteBatchProjection.Object(("id", "Id", scalar), ("name", "Name", scalar));
    batch.Output("id", batch.Read(matchInput, "MatchId"));
    batch.Output("players", batch.Read(matchInput, "JoinedUsers"), RemoteBatchProjection.Sequence(user));
    batch.Output("winners", batch.Read(matchInput, "WinningPlayers"), RemoteBatchProjection.Sequence(user));
    batch.Output("losers", batch.Read(matchInput, "LosingPlayers"), RemoteBatchProjection.Sequence(user));
    batch.Output("games", batch.Read(matchInput, "Games"),
      RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
        ("status", "GameState", scalar),
        ("rankings", "PlayerRankings", RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
          ("id", "ID", scalar), ("result", "Ranking", scalar)))),
        ("players", "Players", RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
          ("id", "User.Id", scalar)))),
        ("winners", "WinningPlayers", RemoteBatchProjection.Sequence(RemoteBatchProjection.Object(
          ("id", "User.Id", scalar)))))));
  }

  private static IList<PlayerResult> ReadMatchResults(Dictionary<string, RemoteBatchValue> values)
  {
    var outcomes = new Dictionary<int, MatchResult>();
    if (values.TryGetValue("finalResults", out var finalResults))
    {
      foreach (var entry in finalResults.Items)
        outcomes[entry.Members["id"].GetScalar<int>()] =
          (MatchResult)entry.Members["result"].GetScalar<int>();
    }
    else
    {
      foreach (var winner in values["winners"].Items)
        outcomes[winner.Members["id"].GetScalar<int>()] = MatchResult.Win;
      foreach (var loser in values["losers"].Items)
        outcomes[loser.Members["id"].GetScalar<int>()] = MatchResult.Loss;
    }

    IList<PlayerResult> results = new List<PlayerResult>();
    foreach (var player in values["players"].Items)
    {
      int id = player.Members["id"].GetScalar<int>();
      if (!outcomes.TryGetValue(id, out var outcome)) continue;
      int wins = 0, losses = 0, draws = 0;
      foreach (var game in values["games"].Items)
      {
        if (game.Members["status"].GetScalar<int>() != (int)GameStatus.Finished ||
            !game.Members["players"].Items.Any(p => p.Members["id"].GetScalar<int>() == id))
          continue;
        var ranking = game.Members["rankings"].Items
          .FirstOrDefault(p => p.Members["id"].GetScalar<int>() == id);
        if (ranking != null)
        {
          int result = ranking.Members["result"].GetScalar<int>();
          if (result == (int)GameResult.Win) wins++;
          else if (result == (int)GameResult.Loss) losses++;
          else draws++;
        }
        else
        {
          var winners = game.Members["winners"].Items;
          if (winners.Count == 0) draws++;
          else if (winners.Any(p => p.Members["id"].GetScalar<int>() == id)) wins++;
          else losses++;
        }
      }
      results.Add(new(player.Members["name"].GetScalar<string>(), outcome, wins, losses, draws));
    }
    return results;
  }
}
