# Play Guide

This guide covers the Play APIs for monitoring matches, tournaments, and leagues. These are the APIs you'll use to track competitive play, build tournament overlays, and monitor event state in real time.

## Overview

MTGO organizes competitive play into three main structures:

- **Matches**: Individual head-to-head series between two players (best-of-one or best-of-three)
- **Tournaments**: Scheduled events with Swiss or single-elimination rounds, standings, and prizes
- **Leagues**: Asynchronous events where players can join and play at their own pace over days or weeks

The `EventManager` class is the central hub for accessing all live events. It provides collections of featured events (visible in the lobby), joined events (events you're participating in), and methods to look up specific events by ID. For leagues specifically, there's also `LeagueManager` which provides league-specific queries.

```csharp
using MTGOSDK.API.Play;             // EventManager, Match
using MTGOSDK.API.Play.Tournaments; // Tournament
using MTGOSDK.API.Play.Leagues;     // League, LeagueManager
```

---

## Accessing Events

The `EventManager` provides several collections for different views into the event system. Which collection you use depends on what you're trying to display or track.

### Featured Events

Featured events are the tournaments visible in MTGO's play lobby. These are events that players can join or spectate:

```csharp
foreach (var tournament in EventManager.FeaturedEvents)
{
  Console.WriteLine($"{tournament.Description}");
  Console.WriteLine($"  Players: {tournament.TotalPlayers}");
  Console.WriteLine($"  Format: {tournament.Format?.Name}");
}
```

The `FeaturedEvents` collection updates as tournaments start, fill up, and complete. The `TotalPlayers` count shows current registration, which is useful for displaying tournament popularity or tracking when events are about to fire. The `Format` property can be null for some event types, so use null-conditional access when displaying it.

### Joined Events

The `JoinedEvents` collection contains all events the current user has joined, including active matches, ongoing tournaments, and league entries:

```csharp
foreach (var evt in EventManager.JoinedEvents)
{
  switch (evt)
  {
    case Match match:
      Console.WriteLine($"Match: {match.Id}, State: {match.State}");
      break;
    case Tournament tournament:
      Console.WriteLine($"Tournament: {tournament.Id}, Round: {tournament.RoundNumber}");
      break;
    case League league:
      Console.WriteLine($"League: {league.Name}, Wins: {league.Wins}");
      break;
  }
}
```

This collection contains mixed types, so pattern matching is the cleanest way to handle each event type differently. A match appears here during gameplay, a tournament appears while you're registered (even between rounds), and a league appears as long as you have an active league entry.

### Looking Up Specific Events

When you know an event ID (from a notification, history record, or user input), you can retrieve it directly:

```csharp
var evt = EventManager.GetEvent(123456);

switch (evt)
{
  case Match match:
    Console.WriteLine($"Match: {match.Id}, State: {match.State}");
    break;
  case Tournament tournament:
    Console.WriteLine($"Tournament: {tournament.Id}, Round: {tournament.RoundNumber}");
    break;
  case League league:
    Console.WriteLine($"League: {league.Name}, Wins: {league.Wins}");
    break;
}
```

`EventManager.GetEvent(int)` returns a `dynamic` event object (or throws `KeyNotFoundException` if the event doesn't exist). Because the exact subtype isn't known until runtime, use pattern matching to handle each event type. You can also look up an event by its `Guid` token via `EventManager.GetEvent(Guid)`. Event IDs are unique across all event types, so a single lookup resolves to whichever event matches that ID.

---

## Working with Tournaments

Tournament objects provide detailed state about scheduled events. You can track round progression, view standings, and access per-player match history. This is the API you'd use to build a tournament tracker or standings overlay.

### Basic Tournament Info

```csharp
Tournament tournament = EventManager.GetEvent(123456);

Console.WriteLine($"{tournament.Description}");
Console.WriteLine($"Round {tournament.RoundNumber} of {tournament.TotalRounds}");
Console.WriteLine($"Players: {tournament.TotalPlayers}");
Console.WriteLine($"State: {tournament.State}");
```

The `Description` property contains the full tournament name as displayed in MTGO. `RoundNumber` increments as rounds complete, and `TotalRounds` tells you how many rounds the tournament will have (which depends on player count for Swiss events). The `State` property indicates whether the tournament is registering, in progress, or completed.

### Standings

The standings collection shows current player rankings, updated after each round:

```csharp
foreach (var standing in tournament.Standings)
{
  Console.WriteLine($"{standing.Rank}. {standing.Player.Name}");
  Console.WriteLine($"   Record: {standing.Record}");
  Console.WriteLine($"   Points: {standing.Points}");
}
```

Standings are ordered by rank, with `Rank` being the player's current position (1 is first place). The `Record` property is a formatted string like "2-0" showing wins and losses. The `Points` property is the point total used for tiebreakers, which follows standard tournament rules (3 points per match win, etc.).

### Match History Per Player

Each standing includes the player's previous matches in the tournament, which is useful for showing a player's path through the event:

```csharp
var standing = tournament.Standings.First();

Console.WriteLine($"Match history for {standing.Player.Name}:");
foreach (var match in standing.PreviousMatches)
{
  string result = match.HasBye ? "Bye" : $"Round {match.Round}";
  Console.WriteLine($"  {result}: {match.State}");
  
  foreach (var game in match.GameStandingRecords)
  {
    Console.WriteLine($"    Game {game.Id}: {game.GameStatus}");
  }
}
```

The `PreviousMatches` collection contains lightweight `MatchStandingRecord` objects with just the essential match data: ID, round number, state, result, and bye status. These are not full `Match` objects because the detailed game state isn't always available for completed matches. The `GameStandingRecords` nested collection shows individual game results within each match.

### Round Information

For a round-by-round view of the tournament, use the `Rounds` collection:

```csharp
foreach (var round in tournament.Rounds)
{
  Console.WriteLine($"Round {round.Number} (Complete: {round.IsComplete})");
  
  foreach (var match in round.Matches)
  {
    var players = string.Join(" vs ", match.Players.Select(p => p.Name));
    Console.WriteLine($"  {players}: {match.State}");
  }
}
```

Each round contains a `Matches` collection with full `Match` objects for that round. The `IsComplete` property tells you whether all matches in the round have finished, which is useful for knowing when pairings for the next round will be available. Unlike `PreviousMatches` in standings, these are live `Match` objects that you can subscribe to for real-time updates.

---

## Working with Leagues

Leagues are asynchronous events that run continuously over days or weeks. Unlike tournaments, there are no scheduled rounds. Players join when ready, get matched on-demand, and can complete their league entry over multiple sessions.

### Active Leagues

```csharp
foreach (var league in LeagueManager.OpenLeagues)
{
  Console.WriteLine($"{league.Name}");
  Console.WriteLine($"  Record: {league.Wins}-{league.Losses} of {league.TotalMatches}");
}
```

The `OpenLeagues` collection contains leagues the user has an active entry in. The `Wins` and `Losses` properties track your win-loss record, while `TotalMatches` tells you how many matches you can play before the entry completes (typically 5 for a traditional league; `MinMatches` is the minimum required to be eligible for prizes). To observe league lifecycle changes, subscribe to the `StateChanged` event, which provides a `LeagueStateEventArgs` describing the transition.

### League vs Tournament

The key difference between leagues and tournaments is match pairing. In a tournament, rounds are scheduled: all players get paired simultaneously, all matches must complete before the next round begins. In a league, pairing happens on-demand: when you click "Play", MTGO finds another player who's also looking for a match and pairs you immediately.

This means league matches can happen any time the player chooses, and there's no waiting for other players to finish their matches. The tradeoff is that there's no bracket progression or elimination drama.

---

## Working with Matches

A match represents a head-to-head series of games between two players. In a best-of-three format (most competitive play), a match contains up to three games. The first player to win two games wins the match.

### Match Properties

```csharp
Match match = EventManager.GetEvent(123456);

Console.WriteLine($"Match ID: {match.Id}");
Console.WriteLine($"State: {match.State}");
Console.WriteLine($"Started: {match.StartTime}");
Console.WriteLine($"Finished: {match.EndTime}");

var players = string.Join(" vs ", match.Players.Select(p => p.Name));
Console.WriteLine($"Players: {players}");
```

The `State` property indicates where the match is in its lifecycle: not started, in progress, sideboarding (between games), or finished. `StartTime` and `EndTime` track when the match began and completed. The `Players` collection contains the two participants, which you can use to identify who's playing.

### Current Game

During an active match, you can access the current game for detailed state:

```csharp
var game = match.CurrentGame;
if (game != null)
{
  Console.WriteLine($"Game {game.Id}: Turn {game.CurrentTurn}");
  Console.WriteLine($"Phase: {game.CurrentPhase}");
}
```

The `CurrentGame` property returns null between games (during sideboarding) or if no game is active yet. When a game is in progress, this gives you the `Game` object for detailed tracking. The `Game` class has extensive properties and events for tracking life totals, zone changes, and card actions, which are covered in the [Games Guide](./games.md).

---

## Subscribing to Events

All play objects expose events for real-time state tracking. This is how you'd build a live tournament tracker or match overlay that updates as the game progresses.

### Match Events

```csharp
match.OnGameStarted += (game) =>
{
  Console.WriteLine($"Game {game.Id} started");
};

match.OnGameEnded += (game) =>
{
  var winners = string.Join(", ", game.WinningPlayers.Select(p => p.Name));
  Console.WriteLine($"Game {game.Id} ended. Winner: {winners}");
};
```

Match events fire at key transition points. `OnGameStarted` fires when a new game begins (including game 1), while `OnGameEnded` fires when a game completes. The callback receives the affected `Game` object, which you can query for detailed state like final life totals or winning players.

### Tournament Events

```csharp
tournament.OnRoundChanged += (round) =>
{
  Console.WriteLine($"Round changed to {round.Number}");
};

tournament.OnStateChanged += (state) =>
{
  Console.WriteLine($"Tournament state changed to {state}");
};
```

Tournament events track bracket progression. The `OnRoundChanged` event fires when a round completes and the next round begins, providing the new `TournamentRound`. The `OnStateChanged` event fires when the tournament transitions between states (e.g., registering, in progress, completed). These are useful for refreshing standings displays or alerting users that their next match is ready. You can also subscribe to `OnStandingsChanged` to be notified when the standings are recalculated after a round.

### Cleaning Up

When you're done tracking an event, clear your event subscriptions to prevent memory leaks:

```csharp
// Clear specific events
match.OnGameStarted.Clear();
match.OnGameEnded.Clear();

// Or clear all events on an object
match.ClearEvents();
tournament.ClearEvents();
```

Event objects in the SDK hold references to your callbacks, which prevents garbage collection. If you're tracking many events over time (like in a long-running application), accumulated subscriptions can cause memory growth. Clearing events when you're done with an object ensures clean resource management.

---

## Next Steps

- [Games Guide](./games.md) - In-game state tracking (zones, cards, actions)
- [History Guide](./history.md) - Completed matches and tournaments
- [Collection Guide](./collection.md) - Decks and cards
