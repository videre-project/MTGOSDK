# Games API Guide

This guide covers the in-game state tracking APIs used for detailed game analysis and logging. By the end, you'll understand how to access game state, subscribe to events, and build game trackers like the one in the Videre Tracker project.

## Overview

When a player joins a match in MTGO, each individual game within that match is represented by a `Game` object. A typical best-of-three match has up to three `Game` instances, each tracking its own turn count, player life totals, zones, and cards.

The Games API operates at a lower level than the Play API covered in the [Play Guide](./play.md). While `Match` and `Tournament` objects tell you who's playing and what the standings are, `Game` objects let you track individual card plays, life total changes, and game actions in real time.

This is the API you'd use to build:
- Game replay analyzers
- Play-by-play loggers
- Statistical tracking tools
- Stream overlays showing game state

---

## Accessing Game Objects

You don't create `Game` objects directly. They come from `Match` instances. When you're tracking a match, you can enumerate all games played so far or get the current game in progress:

```csharp
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;

var match = EventManager.GetMatch(123456);

// See all games played in this match
foreach (var game in match.Games)
{
  Console.WriteLine($"Game {game.Id}: {game.Status}");
}

// Get the game currently being played (null between games)
var currentGame = match.CurrentGame;
```

The `Games` collection grows as games complete. In a best-of-three, you'll see one game initially, then two after the first game ends, and so on. The `CurrentGame` property returns `null` during sideboarding between games.

Games have three key status values: `NotStarted` (pregame/mulligan phase), `Started` (gameplay in progress), and `Finished` (game over). You'll typically want to start tracking when status changes to `Started` and stop when it reaches `Finished`.

---

## Players and Life Totals

Each game has its own `GamePlayer` objects representing the players in that specific game. These are distinct from the `User` objects you'd get from the buddy list or chat. `GamePlayer` includes game-specific state like life total and mana pool.

```csharp
foreach (GamePlayer player in game.Players)
{
  Console.WriteLine($"{player.Name}: {player.Life} life");
  Console.WriteLine($"  Hand: {player.HandCount}, Library: {player.LibraryCount}");
  Console.WriteLine($"  Clock: {player.ChessClock}");
}
```

The count properties (`HandCount`, `LibraryCount`, `GraveyardCount`) give you quick access to zone sizes without needing to enumerate the actual cards. This is useful for displaying game state in overlays.

You can determine whose turn it is and who can take actions using `game.ActivePlayer` and `game.PriorityPlayer`. These update automatically as the game progresses.

---

## Zones and Cards

Every card in the game exists in exactly one zone at a time. Zones can belong to a specific player (like their hand or library) or be shared between players (like the stack).

### Accessing Player Zones

```csharp
GamePlayer player = game.Players[0];

GameZone hand = game.GetGameZone(player, CardZone.Hand);
GameZone library = game.GetGameZone(player, CardZone.Library);
GameZone battlefield = game.GetGameZone(player, CardZone.Battlefield);
GameZone graveyard = game.GetGameZone(player, CardZone.Graveyard);

Console.WriteLine($"{player.Name}'s hand has {hand.Count} cards");
```

### Accessing Shared Zones

```csharp
GameZone stack = game.GetGameZone(CardZone.Stack);
GameZone exile = game.GetGameZone(CardZone.Exile);
```

The stack and exile zones don't belong to any single player, so you call `GetGameZone` with just the zone type.

### Iterating Cards

Each zone has a `Cards` property that returns the `GameCard` objects in that zone:

```csharp
foreach (GameCard card in battlefield.Cards)
{
  string status = card.IsTapped ? "(tapped)" : "";
  Console.WriteLine($"  {card.Name} {status}");
}
```

Cards have different relevant properties depending on where they are. On the battlefield, you can check `IsTapped`, `Power`, `Toughness`, `Damage`, `IsAttacking`, and `IsBlocking`. In the graveyard, most of these don't apply.

One important distinction: `Owner` is the player who started the game with the card, while `Controller` is who currently controls it. These differ when cards get stolen with effects like Control Magic or Bribery.

---

## Tracking Game Events

The real power of the Games API is event-driven tracking. Rather than polling for changes, you subscribe to events that fire when something happens. This is how the Videre Tracker captures every action in real time.

### Turn and Phase Changes

```csharp
game.CurrentTurnChanged += (GameEventArgs e) =>
{
  Console.WriteLine($"Turn {e.Game.CurrentTurn}: {e.Game.ActivePlayer?.Name}");
};

game.OnGamePhaseChange += (CurrentPlayerPhase phase) =>
{
  Console.WriteLine($"  Phase: {phase.CurrentPhase}");
};
```

The `CurrentTurnChanged` event fires once per turn. The `OnGamePhaseChange` event fires multiple times per turn as the game moves through untap, upkeep, draw, main phase, combat, and so on. For basic logging, turn changes are usually sufficient. For detailed replay, you want phase changes too.

### Card Movement

```csharp
game.OnZoneChange += (GameCard card) =>
{
  string from = card.PreviousZone?.Name ?? "nowhere";
  string to = card.Zone?.Name ?? "nowhere";
  Console.WriteLine($"{card.Name} moved from {from} to {to}");
};
```

This event fires whenever a card changes zones: drawing, casting, dying, exiling, bouncing, and so on. The card object has both `Zone` (where it is now) and `PreviousZone` (where it came from), so you can track the movement.

### Life Total Changes

```csharp
game.OnLifeChange += (GamePlayer player) =>
{
  Console.WriteLine($"{player.Name} life: {player.Life}");
};
```

The callback receives the player whose life changed, with their new life total already reflected in the `Life` property.

### Game Actions

Every choice a player makes (casting a spell, activating an ability, choosing a target) is a `GameAction`:

```csharp
game.OnGameAction += (GameAction action) =>
{
  Console.WriteLine($"Action: {action.Name}");
  
  if (action is CardAction cardAction)
  {
    Console.WriteLine($"  Card: {cardAction.Card?.Name}");
  }
};
```

The `GameAction` base class has several subtypes: `CardAction` for spells and abilities, `PrimitiveAction` for OK/Cancel/Pass, `NumericAction` for choosing numbers, and so on. You can use pattern matching to handle specific action types.

### Game Completion

```csharp
game.OnGameResultsChanged += (IList<GamePlayerResult> results) =>
{
  foreach (var result in results)
  {
    Console.WriteLine($"{result.Player.Name}: {result.Result}");
  }
};
```

This event fires when the game ends. Each `GamePlayerResult` contains the player, whether they won or lost, and their remaining clock time.

---

## Building a Game Logger

Here's how you might put this together into a complete tracker. This pattern is based on the Videre Tracker's `GameTracker` class:

```csharp
public class GameLogger : IDisposable
{
  private readonly Game _game;
  private readonly List<string> _log = new();

  public GameLogger(Game game)
  {
    _game = game;
    
    // Capture initial state
    foreach (GamePlayer player in game.Players)
    {
      _log.Add($"Player: {player.Name} ({player.Life} life)");
    }
    
    // Subscribe to events
    game.CurrentTurnChanged += OnTurn;
    game.OnZoneChange += OnZoneChange;
    game.OnLifeChange += OnLifeChange;
    game.OnGameAction += OnAction;
  }

  private void OnTurn(GameEventArgs e) =>
    _log.Add($"=== Turn {e.Game.CurrentTurn} ===");

  private void OnZoneChange(GameCard card) =>
    _log.Add($"{card.Name} -> {card.Zone?.Name}");

  private void OnLifeChange(GamePlayer player) =>
    _log.Add($"{player.Name}: {player.Life} life");

  private void OnAction(GameAction action) =>
    _log.Add($"Action: {action.Name}");

  public void Dispose()
  {
    _game.ClearEvents();
  }
}
```

Several things are worth noting here:

First, we capture the initial state in the constructor before subscribing to events. Otherwise we'd miss the starting life totals.

Second, we use named methods instead of inline lambdas. This makes it possible to unsubscribe later. You can only remove a delegate if you have a reference to the exact method you added.

Third, the `Dispose` method calls `ClearEvents()` on the game object. This removes all our event handlers, preventing memory leaks if the game object lives longer than our logger.

---

## Cleaning Up

The `Game` class doesn't implement `IDisposable`, so you don't dispose it directly. Instead, call `ClearEvents()` to remove all your event subscriptions when you're done tracking:

```csharp
// Remove all handlers from all events
game.ClearEvents();

// Or remove handlers from a specific event
game.OnZoneChange.Clear();
```

Failing to clean up event handlers is a common source of memory leaks. If you create multiple loggers for the same game (or across multiple games), make sure each one cleans up when it's done.

---

## Next Steps

- **[Play Guide](./play.md)** - Higher-level match and tournament tracking
- **[History Guide](./history.md)** - Match history and game replays
- **[Connection Lifecycle](../reference/connection-lifecycle.md)** - Handling disconnects and reconnection

