/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;


namespace MTGOSDK.Tests;

public class Events : EventValidationFixture
{
  [Test]
  public void Test_EventManager()
  {
#pragma warning disable CS8600
    dynamic eventObj = null!;
    using (Log.Suppress())
    {
      eventObj = EventManager.Events
        .Where(e => e.Description != string.Empty)
        .Skip(new Random().Next(0, 50))
        .First();
    }
#pragma warning restore CS8600

    Assert.That(EventManager.GetJoinableEvent(eventObj.Id).Description,
                Is.EqualTo(eventObj.Description));

    Assert.That(EventManager.GetJoinableEvent(eventObj.Token).Description,
                Is.EqualTo(eventObj.Description));

    Assert.That(EventManager.GetEvent(eventObj.Id).Description,
                Is.EqualTo(eventObj.Description));

    Assert.That(EventManager.GetEvent(eventObj.Token).Description,
                Is.EqualTo(eventObj.Description));
  }

  [RateLimit(ms: 300)]
  [TestCase<League>()]
  [TestCase<Match>()]
  [TestCase<Tournament>()]
  [TestCase<Queue>()]
  public void Test_Events<T>() where T : Event<T>
  {
    // For testing, we'll restrict testing to small-sized events.
    T eventObj = GetEvent<T>(e => e.Description != string.Empty &&
                                  e.TotalPlayers <= 16);
    switch (typeof(T).Name)
    {
      case "League":
        ValidateLeague((eventObj as League)!);
        break;
      case "Match":
        ValidateMatch((eventObj as Match)!);
        break;
      case "Tournament":
        ValidateTournament((eventObj as Tournament)!);
        break;
      case "Queue":
        ValidateQueue((eventObj as Queue)!);
        break;
      default:
        ValidateEvent(eventObj);
        break;
    }
  }
}

public class EventValidationFixture : BaseFixture
{
  public T GetEvent<T>(Func<dynamic, bool> predicate = null!) where T : class
  {
#pragma warning disable CS8600
    dynamic eventObj = null!;
    switch (typeof(T).Name)
    {
      case "League":
        eventObj = LeagueManager.Leagues
          .Where(e => predicate?.Invoke(e) ?? true)
          .First();
        break;
      default:
        using (Log.Suppress()) // Exclude Log.Trace messages from test output
        {
          eventObj = EventManager.Events
            .Where(e => e is T && (predicate?.Invoke(e) ?? true))
            .First();
        }
        break;
    }
    Log.Trace("Retrieved event: {0}", eventObj);
#pragma warning restore CS8600

    return (eventObj as T)!;
  }

  public void ValidateEvent<T>(T? eventObj) where T : Event<T>
  {
    Assert.That(eventObj, Is.Not.Null);
    Assert.That(eventObj, Is.InstanceOf<T>());
    Assert.That(eventObj.ToString(), Is.Not.Empty);

    // IEvent properties
    Assert.That(eventObj.Id, Is.GreaterThan(0));
    Assert.That(eventObj.Token, Is.Not.EqualTo(Guid.Empty));
    Assert.That(eventObj.EventType, Is.EqualTo(typeof(T).Name));
    Assert.That(eventObj.Format.ToString(), Is.Not.Empty);
    Assert.That(eventObj.Description, Is.Not.Empty);
    Assert.That(eventObj.TotalPlayers, Is.GreaterThanOrEqualTo(0));
    Assert.That(eventObj.Players,
        eventObj.TotalPlayers == 0 ? Is.Empty : Is.Not.Empty);
    Assert.That(eventObj.MinutesPerPlayer, Is.GreaterThanOrEqualTo(0));
    Assert.That(eventObj.MinimumPlayers, Is.GreaterThanOrEqualTo(0));
    Assert.That(eventObj.MaximumPlayers, Is.GreaterThanOrEqualTo(0));

    Assert.That((bool?)eventObj.IsCompleted, Is.Not.Null);
    Assert.That((bool?)eventObj.IsRemoved, Is.Not.Null);
    Assert.That((bool?)eventObj.HasJoined, Is.Not.Null);
    Assert.That((bool?)eventObj.IsParticipant, Is.Not.Null);
    Assert.That((bool?)eventObj.IsEliminated, Is.Not.Null);

    if (!eventObj.IsParticipant)
      Assert.That(() => eventObj.RegisteredDeck,
          Throws.InstanceOf<InvalidOperationException>());
    else
      Assert.That(eventObj.RegisteredDeck.Id, Is.GreaterThan(0));
  }

  public void ValidateLeague(League league)
  {
    // IEvent properties
    ValidateEvent(league);

    // ILeague properties
    Assert.That(league.Name, Is.Not.Empty);
    Assert.That(league.OpenDate, Is.GreaterThan(DateTime.MinValue));
    Assert.That(league.ActiveDate, Is.GreaterThanOrEqualTo(league.OpenDate));
    Assert.That(league.ClosedDate, Is.GreaterThan(league.ActiveDate));
    Assert.That(league.CompletedDate, Is.GreaterThanOrEqualTo(league.ClosedDate));
    Assert.That(league.JoinedMembers, Is.GreaterThanOrEqualTo(0));
    Assert.That(league.Leaderboard.Count, Is.LessThanOrEqualTo(league.JoinedMembers));
    Assert.That(league.TotalMatches, Is.GreaterThanOrEqualTo(3));
    Assert.That(league.MinMatches, Is.GreaterThanOrEqualTo(3));
    Assert.That((bool?)league.IsPaused, Is.Not.Null);

    foreach(LeaderboardEntry entry in league.Leaderboard)
    {
      Assert.That(entry.Name, Is.Not.Empty);
      Assert.That(entry.TrophyCount, Is.GreaterThanOrEqualTo(0));
      Assert.That(entry.LastTrophyEarnedDate,
          Is.GreaterThanOrEqualTo(DateTime.MinValue));
    }

    // ILeagueLocalParticipant properties
    Assert.That(league.ActiveDeck?.Id, Is.Not.EqualTo(-1));
    Assert.That(league.GameHistory.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(league.MatchNumber, Is.GreaterThanOrEqualTo(0));
    Assert.That(league.MatchesRemaining, Is.LessThanOrEqualTo(league.TotalMatches));
    Assert.That(league.Wins, Is.GreaterThanOrEqualTo(0));
    Assert.That(league.Losses, Is.GreaterThanOrEqualTo(0));
    Assert.That(league.TrophyCount, Is.GreaterThanOrEqualTo(0));
    Assert.That((bool?)league.IsWaitingInMatchQueue, Is.Not.Null);
    Assert.That((bool?)league.IsMatchInProgress, Is.Not.Null);
  }

  public void ValidateTournament(Tournament tournament)
  {
    // IEvent properties
    ValidateEvent(tournament);

    // IQueueBasedEvent properties
    Assert.That(tournament!.StartTime, Is.GreaterThan(DateTime.MinValue));
    Assert.That(tournament.EndTime, Is.GreaterThan(tournament.StartTime));
    Assert.That(tournament.TotalRounds, Is.GreaterThan(0));

    // ITournament properties
    Assert.That(tournament.State, Is.Not.EqualTo(TournamentState.NotSet));
    Assert.That(tournament.TimeRemaining, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
    Assert.That(tournament.CurrentRound, Is.GreaterThanOrEqualTo(0));
    Assert.That(tournament.Rounds.Count, Is.EqualTo(tournament.CurrentRound));
    Assert.That(tournament.Standings.Count,
        Is.GreaterThanOrEqualTo(tournament.TotalPlayers * 0.95).Or.EqualTo(0));

    Assert.That((bool?)tournament.HasBye, Is.Not.Null);
    Assert.That((bool?)tournament.InPlayoffs, Is.Not.Null);

    foreach(StandingRecord standing in tournament.Standings)
    {
      Assert.That(standing.Rank,
          tournament.CurrentRound <= 1 ? Is.GreaterThanOrEqualTo(0) : Is.GreaterThan(0));
      Assert.That(standing.Player, Is.Not.Null);
      Assert.That(standing.Points, Is.GreaterThanOrEqualTo(0));
      Assert.That(standing.OpponentMatchWinPercentage, Is.Not.Empty);
      Assert.That(standing.GameWinPercentage, Is.Not.Empty);
      Assert.That(standing.OpponentGameWinPercentage, Is.Not.Empty);
      Assert.That(standing.PreviousMatches,
          tournament.CurrentRound == 0 ? Is.Empty : Is.Not.Empty);

      foreach(MatchStandingRecord match in standing.PreviousMatches)
      {
        Assert.That(match.Id, Is.GreaterThan(0));
        Assert.That(match.Round, Is.GreaterThan(0));
        Assert.That(match.Round, Is.LessThanOrEqualTo(tournament.CurrentRound));
        Assert.That(match.State, Is.Not.EqualTo(MatchState.Invalid));
        Assert.That(match.HasBye, match.Players.Count == 1 ? Is.True : Is.False);
        Assert.That(match.Players.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(match.WinningPlayerIds.Count, Is.LessThanOrEqualTo(3));
        Assert.That(match.LosingPlayerIds.Count, Is.LessThanOrEqualTo(2));
        Assert.That(match.GameStandingRecords, match.HasBye ? Is.Empty : Is.Not.Empty);

        foreach(GameStandingRecord game in match.GameStandingRecords)
        {
          Assert.That(game.Id, Is.GreaterThan(0));
          Assert.That(game.GameState, Is.Not.EqualTo(GameState.Invalid));
          // Assert.That(game.CompletedDuration,
          //   game.GameState == GameState.Finished
          //     ? Is.GreaterThan(TimeSpan.Zero)
          //     : Is.AnyOf(Is.Null, TimeSpan.Zero));
          Assert.That(game.WinnerIds.Count, Is.LessThanOrEqualTo(1));
        }
      }
    }
  }

  public void ValidateQueue(Queue queue)
  {
    // IEvent properties
    ValidateEvent(queue);

    // IQueue properties
    Assert.That(queue.CurrentState, Is.Not.EqualTo(QueueState.NotSet));
  }

  public void ValidateMatch(Match match)
  {
    // IEvent properties
    ValidateEvent(match);

    // IMatch properties
    Assert.That(match.MatchId, Is.GreaterThan(0));
    Assert.That(match.MatchToken, Is.Not.EqualTo(Guid.Empty));
    Assert.That(match.State, Is.Not.EqualTo(MatchState.Invalid));
    // Assert.That(match.Creator?.Id, Is.Not.EqualTo(-1));
    // Assert.That(match.ChallengeReceiver?.Id, Is.Not.EqualTo(-1));
    Assert.That(match.ChallengeText, Is.Not.Empty);
    Assert.That(match.Games.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(match.StartTime, Is.GreaterThan(DateTime.MinValue));
    if (match.StartTime > DateTime.MinValue &&
        match.State >= MatchState.GameStarted)
    {
      Assert.That(match.CurrentGame.Id, Is.GreaterThanOrEqualTo(0));
      // Assert.That(match.EndTime, Is.GreaterThan(match.StartTime));
      Assert.That(match.EndTime, Is.GreaterThanOrEqualTo(DateTime.MinValue));
      // Assert.That(match.SideboardingEnds, Is.GreaterThan(match.StartTime));
      Assert.That(match.SideboardingEnds,
        Is.GreaterThanOrEqualTo(DateTime.Parse("1970-01-01",
                                                CultureInfo.InvariantCulture,
                                                DateTimeStyles.AssumeUniversal)));
    }
    Assert.That(match.WinningPlayers.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(match.LosingPlayers.Count, Is.GreaterThanOrEqualTo(0));

    foreach(Game game in match.Games)
    {
      Assert.That(game.Id, Is.GreaterThan(0));
      Assert.That(game.ServerGuid, Is.Not.EqualTo(Guid.Empty));
      Assert.That(game.State, Is.Not.EqualTo(GameState.Invalid));
    }
  }
}
