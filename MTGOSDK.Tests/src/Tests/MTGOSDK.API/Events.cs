/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class Events : EventValidationFixture
{
  [Test]
  public void Test_EventManager()
  {
    // Grab a random event type to test the `Events` property
    dynamic eventObj1 = null!;
    using (Log.Suppress())
    {
      eventObj1 = EventManager.Events
        .Where(e => e.Description != string.Empty)
        .First();
    }
    Assert.That(eventObj1, Is.Not.Null,
      "Unable to find an event in the events list.");
    ValidateEvent(eventObj1);

    // Grab a random tournament to test the `FeaturedEvents` property
    Tournament? eventObj2 = null!;
    using (Log.Suppress())
    {
      eventObj2 = EventManager.FeaturedEvents
        .Where(e => e.Description != string.Empty)
        .FirstOrDefault();
    }
    Assert.That(eventObj2, Is.Not.Null,
      "Unable to find a tournament in the featured events list.");
    ValidateEvent(eventObj2);

    // Grab a random tournament to ensure we test other event structures.
    // We use the Events collection instead to query previous tournaments as well.
    bool hasPlayoffs = eventObj2.HasPlayoffs;
    Tournament? eventObj3 = null!;
    using (Log.Suppress())
    {
      eventObj3 = EventManager.Events
        .Where(e => Try<bool>(() => e.HasPlayoffs == hasPlayoffs))
        .FirstOrDefault();
    }
    Assert.That(eventObj3, Is.Not.Null,
      string.Format("Unable to find a tournament {0} playoffs.",
                    hasPlayoffs ? "with top 8" : "without top 8"));
    ValidateEvent(eventObj3);

    // Ensure that the event manager has a valid list of joined events
    Assert.That(EventManager.JoinedEvents, Is.Not.Null);
    Assert.That(EventManager.JoinedEvents, Is.InstanceOf<IEnumerable<dynamic>>());

    // Ensure that invalid event ids or tokens throw an exception
    Assert.That(() => EventManager.GetEvent(-1),
                Throws.TypeOf<ArgumentException>());
    Assert.That(() => EventManager.GetEvent(1),
                Throws.TypeOf<KeyNotFoundException>());
    Assert.That(() => EventManager.GetEvent(Guid.Empty),
                Throws.TypeOf<ArgumentException>());
    Assert.That(() => EventManager.GetEvent(new Guid("00000000-0000-0000-0000-000000000001")),
                Throws.TypeOf<KeyNotFoundException>());
  }

  [RateLimit(ms: 300)]
  [TestCaseGeneric<League>()]
  [TestCaseGeneric<Match>()]
  [TestCaseGeneric<Tournament>()]
  [TestCaseGeneric<Queue>()]
  public void Test_Events<T>() where T : Event
  {
    // For testing, we'll restrict testing to small-sized events.
    T eventObj = GetEvent<T>(e =>
    {
      // Filter out mid-size events that don't contain an event description.
      if (e.Description == string.Empty || e.TotalPlayers > 32)
        return false;

      // Ensure that any retrieved events have already started.
      if (typeof(T) == typeof(League))
        return Try<bool>(() => (e as League)!.JoinedMembers > 0 &&
                               (e as League)!.Leaderboard.Count >= 0);
      if (typeof(T) == typeof(Match))
        return Try<bool>(() => (e as Match)!.State >= MatchState.GameStarted);
      if (typeof(T) == typeof(Tournament))
        return Try<bool>(() => (e as Tournament)!.HasPlayoffs);
      if (typeof(T) == typeof(Queue))
        return Try<bool>(() => (e as Queue)!.CurrentState >= QueueState.NotJoined);

      return true;
    });

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

  [Test]
  public void Test_PlayFormat()
  {
    var deck = CollectionManager.Decks.First();
    var format = deck.Format!;
    ValidatePlayFormat(format);

    // Verify that any deck passed does not return an error.
    Assert.That((bool?)format.IsDeckLegal(deck), Is.Not.Null);
    Assert.DoesNotThrow(() => format.SetDeckLegality(deck));
  }
}

public class EventValidationFixture : BaseFixture
{
  public T GetEvent<T>(Predicate predicate = null!) where T : class
  {
    dynamic eventObj = null!;
    switch (typeof(T).Name)
    {
      case "League":
        eventObj = Filter<T>(LeagueManager.Leagues, predicate);
        break;
      default:
        using (Log.Suppress()) // Exclude Log.Trace messages from test output
        {
          eventObj = Filter<T>(EventManager.Events, predicate);
        }
        break;
    }
    Log.Trace("Retrieved event: {0}", eventObj);

    return (eventObj as T)!;
  }

  public void ValidateEvent<T>(T? eventObj) where T : Event
  {
    Assert.That(eventObj, Is.Not.Null);
    Assert.That(eventObj, Is.InstanceOf<T>());
    Assert.That(eventObj!.ToString(), Is.Not.Empty);

    // Test that event can be retrieved by ID or event token
    if (typeof(T) == typeof(League))
    {
      Assert.That(LeagueManager.GetLeague(eventObj.Id).Description,
                  Is.EqualTo(eventObj.Description));
      Assert.That(LeagueManager.GetLeague(eventObj.Token).Description,
                  Is.EqualTo(eventObj.Description));
    }
    else
    {
      Assert.That(EventManager.GetEvent(eventObj.Id).Description,
                  Is.EqualTo(eventObj.Description));
      Assert.That(EventManager.GetEvent(eventObj.Token).Description,
                  Is.EqualTo(eventObj.Description));
    }

    // IEvent properties
    Assert.That(eventObj.Id, Is.GreaterThan(0));
    Assert.That(eventObj.Token, Is.Not.EqualTo(Guid.Empty));
    ValidatePlayFormat(eventObj.Format);
    Assert.That(eventObj.Description, Is.Not.Empty);
    Assert.That(eventObj.TotalPlayers, Is.GreaterThanOrEqualTo(0));
    Assert.That(eventObj.Players,
        eventObj.TotalPlayers == 0 ? Is.Empty : Is.Not.Empty);
    Assert.That(eventObj.RegisteredDeck?.Id,
      eventObj.IsParticipant ? Is.GreaterThan(0) : Is.Null);
    Assert.That(eventObj.MinutesPerPlayer, Is.GreaterThanOrEqualTo(0));
    Assert.That(eventObj.MinimumPlayers, Is.GreaterThanOrEqualTo(0));
    Assert.That(eventObj.MaximumPlayers, Is.GreaterThanOrEqualTo(0));

    Assert.That((bool?)eventObj.IsCompleted, Is.Not.Null);
    Assert.That((bool?)eventObj.IsRemoved, Is.Not.Null);
    Assert.That((bool?)eventObj.HasJoined, Is.Not.Null);
    Assert.That((bool?)eventObj.IsParticipant, Is.Not.Null);
    Assert.That((bool?)eventObj.IsEliminated, Is.Not.Null);
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
    Assert.That(league.Leaderboard.Count,
        league.JoinedMembers > 0 ? Is.GreaterThan(0) : Is.EqualTo(0));
    Assert.That(league.TotalMatches, Is.GreaterThanOrEqualTo(3));
    Assert.That(league.MinMatches, Is.GreaterThanOrEqualTo(1));
    Assert.That((bool?)league.IsPaused, Is.Not.Null);

    foreach(LeaderboardEntry entry in league.Leaderboard.Take(5))
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

    // Check if the league is in the client's list of open leagues
    Assert.That(LeagueManager.OpenLeagues,
      league.ActiveDeck != null ? Has.Member(league) : Has.No.Member(league));
  }

  private void ValidateEventStructure(EventStructure eventStructure)
  {
    Assert.That(eventStructure, Is.Not.Null);
    Assert.That(eventStructure.Name, Is.Not.Empty);

    Assert.That(eventStructure.IsConstructed,
        Is.EqualTo(!eventStructure.IsLimited &&
                   !eventStructure.IsDraft && !eventStructure.IsSealed));
    Assert.That(eventStructure.IsDraft,
        Is.EqualTo(eventStructure.IsLimited &&
                   !eventStructure.IsConstructed && !eventStructure.IsSealed));
    Assert.That(eventStructure.IsSealed,
        Is.EqualTo(eventStructure.IsLimited &&
                   !eventStructure.IsConstructed && !eventStructure.IsDraft));

    Assert.That(eventStructure.IsSingleElimination,
        Is.EqualTo(!eventStructure.IsSwiss));
    Assert.That(eventStructure.IsSwiss,
        Is.EqualTo(!eventStructure.IsSingleElimination));
    Assert.That(eventStructure.HasPlayoffs,
        Is.EqualTo(eventStructure.Name.Contains("(with top 8)")));
  }

  public void ValidateTournament(Tournament tournament)
  {
    // IEvent properties
    ValidateEvent(tournament);

    Assert.That(tournament.EntryFee, Is.Not.Empty);
    Assert.That(tournament.EntryFee.Count, Is.GreaterThan(0));
    EntryFeeSuite.EntryFee entryFee = tournament.EntryFee[0];
    Assert.That(entryFee.Id, Is.GreaterThan(-1));
    Assert.That(entryFee.Count, Is.GreaterThan(0));
    Assert.That(entryFee.Item.Id, Is.EqualTo(entryFee.Id));

    Assert.That(tournament.Prizes, Is.Not.Empty);
    Assert.That(tournament.Prizes.Count, Is.GreaterThan(0));
    IList<EventPrize> firstPrizes = tournament.Prizes.First().Value;
    Assert.That(firstPrizes, Is.Not.Empty);
    Assert.That(firstPrizes.Count, Is.GreaterThan(0));
    EventPrize firstPrize = firstPrizes.First();
    Assert.That(firstPrize.Id, Is.GreaterThan(-1));
    Assert.That(firstPrize.Count, Is.GreaterThan(0));
    Assert.That(firstPrize.Item.Id, Is.EqualTo(firstPrize.Id));

    // IQueueBasedEvent properties
    ValidateEventStructure(tournament.EventStructure);
    Assert.That(tournament.StartTime, Is.GreaterThan(DateTime.MinValue));
    Assert.That(tournament.EndTime, Is.GreaterThan(tournament.StartTime));
    Assert.That(tournament.TotalRounds, Is.GreaterThan(0));
    Assert.That(tournament.TotalRounds,
      Is.LessThanOrEqualTo(Math.Max(
        Tournament.GetNumberOfRounds(tournament.TotalPlayers),
        Tournament.GetNumberOfRounds(tournament.MinimumPlayers))));

    // ITournament properties
    Assert.That(tournament.State, Is.Not.EqualTo(TournamentState.NotSet));
    Assert.That(tournament.TimeRemaining, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
    Assert.That(tournament.CurrentRound, Is.GreaterThanOrEqualTo(0));
    Assert.That(tournament.Rounds.Count, Is.EqualTo(tournament.CurrentRound));
    Assert.That(tournament.Standings.Count,
        Is.GreaterThanOrEqualTo(
            Math.Floor(tournament.TotalPlayers * 0.95)).Or.EqualTo(0));

    Assert.That((bool?)tournament.HasBye, Is.Not.Null);
    Assert.That((bool?)tournament.InPlayoffs, Is.Not.Null);
    foreach(TournamentRound round in tournament.Rounds.Take(3))
    {
      Assert.That(round.Number, Is.GreaterThan(0));
      Assert.That(round.IsComplete,
          round.Number > tournament.CurrentRound || tournament.IsCompleted
            ? Is.True
            // Can be true or false if the tournament is still in progress
            : Is.AnyOf(true, false));
      Assert.That(round.Matches.Count, Is.GreaterThanOrEqualTo(0));
      Assert.That(round.StartTime, Is.GreaterThanOrEqualTo(tournament.StartTime));
      Assert.That(round.UsersWithByes.Take(3), Has.All.Not.Null);
    }

    foreach(StandingRecord standing in tournament.Standings.Take(3))
    {
      Assert.That(standing.Rank,
          tournament.CurrentRound <= 1 ? Is.GreaterThanOrEqualTo(0) : Is.GreaterThan(0));
      Assert.That(standing.Player, Is.Not.Null);
      Assert.That(standing.Points, Is.GreaterThanOrEqualTo(0));

      string record = standing.Record;
      Assert.That(record, Is.Not.Null.Or.Empty);
      string[] recordParts = record.Split('-');
      Assert.That(recordParts.Length, Is.EqualTo(3));
      Assert.That(int.TryParse(recordParts[0], out int wins), Is.True);
      Assert.That(int.TryParse(recordParts[1], out int losses), Is.True);
      Assert.That(int.TryParse(recordParts[2], out int ties), Is.True);
      Assert.That(wins + losses + ties,
        Is.GreaterThanOrEqualTo(
          standing.PreviousMatches
            .Count(m => !m.HasBye &&
                   m.State.HasFlag(MatchState.MatchCompleted))));

      Assert.That(standing.OpponentMatchWinPercentage, Is.Not.Empty);
      Assert.That(standing.GameWinPercentage, Is.Not.Empty);
      Assert.That(standing.OpponentGameWinPercentage, Is.Not.Empty);
      Assert.That(standing.PreviousMatches,
          tournament.CurrentRound == 0 ? Is.Empty : Is.Not.Empty);

      foreach(MatchStandingRecord match in standing.PreviousMatches)
      {
        Assert.That(match.Id,
          match.HasBye ? Is.EqualTo(-1) : Is.GreaterThan(0));
        Assert.That(match.Round, Is.GreaterThan(0));
        Assert.That(match.Round, Is.LessThanOrEqualTo(tournament.CurrentRound));
        Assert.That(match.State,
          match.HasBye
            ? Is.EqualTo(MatchState.Invalid)
            : Is.Not.EqualTo(MatchState.Invalid));
        Assert.That(match.HasBye, match.Players.Count == 1 ? Is.True : Is.False);
        Assert.That(match.Players.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(match.WinningPlayerIds.Count, Is.LessThanOrEqualTo(3));
        Assert.That(match.LosingPlayerIds.Count, Is.LessThanOrEqualTo(2));
        Assert.That(match.GameStandingRecords,
          match.HasBye ? Is.Empty : Is.Not.Empty);

        foreach(GameStandingRecord game in match.GameStandingRecords)
        {
          Assert.That(game.Id, Is.GreaterThan(0));
          Assert.That(game.GameStatus, Is.Not.EqualTo(GameStatus.Invalid));
          Assert.That(game.CompletedDuration,
            game.GameStatus == GameStatus.Finished
              ? Is.GreaterThan(TimeSpan.Zero)
              : Is.AnyOf(Is.Null, TimeSpan.Zero));
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
    ValidateEventStructure(queue.EventStructure);
  }

  public void ValidateMatch(Match match)
  {
    // IEvent properties
    ValidateEvent(match);

    // IMatch properties
    Assert.That(match.Id, Is.GreaterThan(0));
    Assert.That(match.Token, Is.Not.EqualTo(Guid.Empty));
    Assert.That(match.State, Is.Not.EqualTo(MatchState.Invalid));
    Assert.That(match.IsComplete,
        Is.EqualTo(true).Or.EqualTo(
          match.State == (MatchState.MatchCompleted | MatchState.GameClosed)));
    Assert.That(match.Creator?.Id, Is.Not.EqualTo(-1));
    Assert.That(match.ChallengeReceiver?.Id, Is.Not.EqualTo(-1));
    Assert.That(match.ChallengeText, Is.Not.Empty);
    Assert.That(match.Games.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(match.StartTime, Is.GreaterThan(DateTime.MinValue));
    if (match.StartTime > DateTime.MinValue &&
        match.State >= MatchState.GameStarted)
    {
      Assert.That(match.CurrentGame?.Id,
        match.Games.Count > 0 ? Is.GreaterThanOrEqualTo(0) : Is.Null);
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
      Assert.That(game.Status, Is.Not.EqualTo(GameStatus.Invalid));
    }
  }

  public void ValidatePlayFormat(PlayFormat format)
  {
    // IPlayFormat properties
    Assert.That(format, Is.Not.Null);
    Assert.That(format.Name, Is.Not.Empty);
    Assert.That(format.Code, Is.Not.Empty);
    Assert.That(format.MinDeckSize, Is.GreaterThan(0));
    Assert.That(format.MaxDeckSize, Is.GreaterThanOrEqualTo(format.MinDeckSize));
    Assert.That(format.MaxCopiesPerCard, Is.GreaterThanOrEqualTo(0));
    Assert.That(format.MaxSideboardSize, Is.GreaterThanOrEqualTo(0));
    Assert.That(format.Type, Is.Not.EqualTo(PlayFormatType.Null));
    Assert.That(format.LegalSets.Take(1),
      format.MinDeckSize == 40 ? Is.Empty : Is.Not.Empty);
    Assert.That(format.BasicLands.Take(1), Is.Not.Empty);

    // IPlayFormat methods
    var card = CollectionManager.GetCard("Colossal Dreadmaw");
    Assert.That((bool?)format.IsCardLegal(card), Is.Not.Null);
    Assert.That((bool?)format.IsCardRestricted(card), Is.Not.Null);
    Assert.That((bool?)format.IsCardBanned(card), Is.Not.Null);
    Assert.That((string?)format, Is.EqualTo(format.Name));
  }
}
