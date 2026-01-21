# History Guide

This guide covers the History and Replay APIs for accessing completed matches, tournaments, and game replays. These APIs are useful for replay analysis, statistics tracking, building match history displays, and analyzing play patterns.

## Overview

The history system stores records of past matches and tournaments in a local file on the user's machine. When MTGO starts, it loads this history file, making your past event data available through the SDK. The history persists across sessions, so you can access matches from previous play sessions, days, or even months ago.

History records are lightweight summaries, not full game replays. They contain metadata like match IDs, timestamps, and results, which you can use as references to load detailed replay data when needed through the Replay APIs.

```csharp
using MTGOSDK.API.Play.History;
```

---

## Checking History Status

Before accessing history, verify it's loaded:

```csharp
if (!HistoryManager.HistoryLoaded)
{
  Console.WriteLine("History not available yet");
  return;
}
```

History may not be loaded in several situations:

- **During startup**: MTGO is still initializing and hasn't read the history file yet
- **Fresh install**: This is a new MTGO installation with no history file
- **Corrupted file**: The history file exists but couldn't be parsed
- **Guest login**: Some login modes don't load history

In production code, handle the unloaded case gracefully rather than assuming history is always available. You can also wait for history to load by using `Client.WaitForClientReady()`, which ensures all client subsystems including history are fully initialized before your code proceeds.

---

## Iterating History

The `Items` collection contains all historical matches and tournaments:

```csharp
foreach (var item in HistoryManager.Items)
{
  switch (item)
  {
    case HistoricalMatch match:
      Console.WriteLine($"Match {match.Id}");
      Console.WriteLine($"  Games: {match.GameIds.Count()}");
      break;
      
    case HistoricalTournament tournament:
      Console.WriteLine($"Tournament {tournament.Id}");
      Console.WriteLine($"  Matches: {tournament.Matches.Count()}");
      break;
  }
}
```

The history contains both `HistoricalMatch` and `HistoricalTournament` objects mixed together, ordered by time with most recent first. Use pattern matching to handle each type appropriately.

`HistoricalMatch` objects contain the game IDs from that match. These IDs can be used to request replay data if you want to analyze the specific games. The match also contains metadata like timestamp, format, and result.

`HistoricalTournament` objects contain references to all matches played in that tournament, along with tournament metadata like format, final standing, and prize payout. Each match reference links back to a `HistoricalMatch` with its own game IDs.

Note that history items are lightweight summary objects designed for quick enumeration. They contain IDs and basic metadata, not the full game state or card-by-card replay data. To view the actual gameplay, you need to request a replay using the game IDs.

---

## Requesting Replays

The `ReplayManager` class handles loading and playing back game replays. Replays let you watch a past game as if it were happening live, with full access to the game state through the Games API.

### Checking Replay Availability

Before requesting a replay, verify that replays are allowed:

```csharp
if (!ReplayManager.ReplaysAllowed)
{
  Console.WriteLine("Replays are currently disabled by the server");
  return;
}
```

MTGO's servers can disable replays during maintenance or for specific events. The `ReplaysAllowed` property reflects the current server policy.

### Starting a Replay

To watch a past game, use `RequestReplay` with the game ID from your history:

```csharp
var match = HistoryManager.Items.OfType<HistoricalMatch>().First();
int gameId = match.GameIds.First();

bool started = await ReplayManager.RequestReplay(gameId);
if (started)
{
  Console.WriteLine("Replay started successfully");
}
else
{
  Console.WriteLine("Failed to start replay");
}
```

The `RequestReplay` method is async because it contacts the server and waits for the replay to initialize. It returns `true` once the replay is active and ready for viewing. The request can fail if the game is too old (replays have a retention period), the server is unavailable, or the game ID is invalid.

### Accessing the Active Replay

Once a replay is running, you can access its state through `ReplayManager.ActiveReplay`:

```csharp
var replay = ReplayManager.ActiveReplay;
if (replay != null)
{
  Console.WriteLine($"Replay state: {replay.State}");
  Console.WriteLine($"Game ID: {replay.Game.Id}");
  Console.WriteLine($"Turn: {replay.Game.CurrentTurn}");
}
```

The `ActiveReplay` property returns `null` when no replay is in progress. When a replay is active, you get a `Replay` object that wraps the replay session.

The `State` property tracks the replay lifecycle:
- `RequestSent`: Request submitted, waiting for server response
- `Connecting`: Server is setting up the replay session
- `Active`: Replay is running and viewable

The `Game` property gives you a full `Game` object for the replayed game. You can use all the same properties and events from the [Games Guide](./games.md) to inspect the game state, players, zones, and cards.

### Checking Replay Status

You can check if a replay is active for a specific game:

```csharp
int gameId = 123456;
if (ReplayManager.IsReplayActive(gameId))
{
  Console.WriteLine($"Replay for game {gameId} is in progress");
}
```

This is useful when you want to verify a specific game is being replayed, rather than checking if any replay is active.

### Handling Replay Errors

The `Replay` object exposes a `ReplayError` event for handling problems during playback:

```csharp
var replay = ReplayManager.ActiveReplay;
if (replay != null)
{
  replay.ReplayError += (sender, args) =>
  {
    Console.WriteLine($"Replay error: {args}");
  };
}
```

Errors can occur if the connection to the replay server is lost, the replay data is corrupted, or the server terminates the session.

---

## Next Steps

- [Play Guide](./play.md) - Live matches and tournaments
- [Games Guide](./games.md) - In-game state tracking (works with replays too)
