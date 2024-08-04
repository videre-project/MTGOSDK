/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.API.Play.History;
using MTGOSDK.Core.Logging;

using HistoricalItem = MTGOSDK.API.Play.History.HistoricalItem<object>;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class History : HistoryValidationFixture
{
  [Test]
  public void Test_HistoryManager()
  {
    Assert.That(HistoryManager.HistoryLoaded, Is.True);
    Assert.That(HistoryManager.Items, Is.Not.Empty);
  }

  // [RateLimit(ms: 300)]
  // [TestCase<HistoricalItem>()]
  [TestCase<HistoricalMatch>()]
  // [TestCase<HistoricalTournament>()]
  public void Test_HistoricalItems<T>()
    where T : class
  {
    T eventObj = GetHistoricalItem<T>();
    switch (typeof(T).Name)
    {
      case "HistoricalItem":
        ValidateHistoricalItem((eventObj as HistoricalItem)!);
        break;
      case "HistoricalMatch":
        ValidateHistoricalMatch((eventObj as HistoricalMatch)!);
        break;
      case "HistoricalTournament":
        ValidateHistoricalTournament((eventObj as HistoricalTournament)!);
        break;
      default:
        Assert.Fail($"Unknown historical item type: {typeof(T).Name}");
        break;
    }
  }
}

public class HistoryValidationFixture : BaseFixture
{
  public static T GetHistoricalItem<T>(Func<dynamic, bool> predicate = null!)
    where T : class
  {
    dynamic eventObj = null!;
    using (Log.Suppress()) // Exclude Log.Trace messages from test output
    {
      eventObj = HistoryManager.Items
        .Where(e => e is T && (predicate?.Invoke(e) ?? true))
        .First();
    }
    Log.Trace("Retrieved historical item: {0}", eventObj);

    return (eventObj as T)!;
  }

  public void ValidateHistoricalItem(dynamic item)
  {
    Assert.That(item.Id, Is.GreaterThan(0));
    // Assert.That(item.Token, Is.Not.EqualTo(Guid.Empty));
    Assert.That(item.StartTime, Is.GreaterThan(DateTime.MinValue));

    Log.Debug("ID: {0}", item.Id);
    Log.Debug("Start time: {0}", item.StartTime);
  }

  public void ValidateHistoricalMatch(HistoricalMatch match)
  {
    ValidateHistoricalItem(match);

    Assert.That(match.Opponents.Count,
      match.GameIds.Count > 1
        ? Is.GreaterThan(0)
        // Account for solataire games
        : Is.GreaterThanOrEqualTo(0));

    Assert.That(match.GameIds, Is.Not.Empty);
    Assert.That(match.GameWins, Is.GreaterThanOrEqualTo(0));
    Assert.That(match.GameWins, Is.LessThanOrEqualTo(2));
    Assert.That(match.GameLosses, Is.GreaterThanOrEqualTo(0));
    Assert.That(match.GameLosses, Is.LessThanOrEqualTo(2));
    Assert.That(match.GameTies, Is.LessThanOrEqualTo(1));

    int totalGames = match.GameWins + match.GameLosses + match.GameTies;
    Assert.That(totalGames, Is.EqualTo(match.GameIds.Count));
  }

  public void ValidateHistoricalTournament(HistoricalTournament tournament)
  {
    ValidateHistoricalItem(tournament);

    Assert.That(tournament.Matches, Is.Not.Empty);
    Assert.That(tournament.MatchWins, Is.GreaterThanOrEqualTo(0));
    Assert.That(tournament.MatchLosses, Is.GreaterThanOrEqualTo(0));

    int totalMatches = tournament.MatchWins + tournament.MatchLosses;
    Assert.That(totalMatches, Is.EqualTo(tournament.Matches.Count));
    foreach (var match in tournament.Matches)
      ValidateHistoricalMatch(match);
  }
}
