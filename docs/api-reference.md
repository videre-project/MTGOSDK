# MTGOSDK API Reference

MTGOSDK organizes its APIs by feature domain, providing direct access to MTGO session management, card collections, gameplay state, chat, user profiles, and client settings. These APIs support automation, structured data retrieval, and real-time event responses.

For installation and setup, see the [Getting Started Guide](./getting-started.md).

## Table of Contents
- [Client](#client)
- [Collection](#collection)
- [Play](#play)
- [History](#history)
- [Chat](#chat)
- [Users](#users)
- [Trade](#trade)
- [Settings](#settings)
- [Toasts and Dialogs](#toasts-and-dialogs)

---

## Client

This section covers APIs for launching, authenticating, and managing the MTGO client session. Use these to automate login, attach to running sessions, and control the client lifecycle.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.API</code></td>
    <td><a href="/MTGOSDK/src/API/Client.cs"><code>Client</code></a></td>
    <td>Manages MTGO process and session</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/ClientOptions.cs"><code>ClientOptions</code></a></td>
    <td>Configuration for Client</td>
  </tr>
</table>


To work with sessions, import the core client types:

```csharp
using MTGOSDK.API; // Client, ClientOptions
```

**Example: Automating Client Launch and Login**

The `Client` class establishes communication with a running MTGO process. For automation, the client can launch MTGO, perform authentication, and manage cleanup:

```csharp
using var client = new Client(new ClientOptions
{
  CreateProcess = true,
  CloseOnExit = true,
  AcceptEULAPrompt = true
});
await client.LogOn("username", password);

// Interact with the MTGO process

await client.LogOff();
```

**Example: Attaching to a Running Session**

When integrating with an MTGO session started by the user, the same API attaches to the running process and allows inspection and event monitoring:

```csharp
using var client = new Client(new ClientOptions());
Console.WriteLine($"Current user: {client.CurrentUser.Name}");
```

Many APIs operate independently of session control, and accessing any API class will automatically initialize the client if it is not already running. For example, to access the collection manager:

```csharp
using MTGOSDK.API.Collection; // CollectionManager

// This will implicitly connect to the MTGO client and dispose automatically
foreach (var deck in CollectionManager.Decks)
{
  Console.WriteLine(deck.Name);
}
```

However, it is recommended to explicitly manage the client lifecycle for long-running applications or automation scripts to ensure proper resource cleanup. In case the MTGO client is closed or crashes, the `Client` instance can be re-instantiated which will reset all references to other API classes.

Once the client is disposed, the client releases all remote handles and cached data. Additionally, if the client launched MTGO, it optionally handles logout and process termination.

---

## Collection

This section describes APIs for managing decks, cards, and binders. Use these to report, analyze, and export your collection data.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="4"><code>MTGOSDK.API.Collection</code></td>
    <td><a href="/MTGOSDK/src/API/Collection/CollectionManager.cs"><code>CollectionManager</code></a></td>
    <td>Manages decks, cards, binders</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Collection/Deck.cs"><code>Deck</code></a></td>
    <td>Represents a deck</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Collection/Card.cs"><code>Card</code></a></td>
    <td>Represents a card</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Collection/Binder.cs"><code>Binder</code></a></td>
    <td>Represents a binder (collection grouping)</td>
  </tr>
</table>

To access collection features, import the collection API:

```csharp
using MTGOSDK.API.Collection; // CollectionManager, Card, Deck, Binder
```

**Example: Exporting Your Collection**

To snapshot the full collection for export or analytics, use `GetFrozenCollection`:

```csharp
// Retrieve the main collection grouping object
var collection = CollectionManager.Collection
  ?? throw new InvalidOperationException("Collection not loaded.");

// Since the collection can contain thousands of unique card objects,
// we use GetFrozenCollection to create a compact, immutable snapshot
// that can be quickly serialized and read for the whole collection.
var frozenCollection = collection.GetFrozenCollection.ToArray();
Console.WriteLine($"Collection ({collection.ItemCount} items)");
foreach (var card in frozenCollection.Take(25))
{
  Console.WriteLine($"{card.Quantity}x {card.Name} ({card.Id})");
}
```

**Example: Accessing Binders**

Binders are special groupings in your collection, such as wishlists or mega binders. You can access binders and inspect their properties:

```csharp
using MTGOSDK.API.Collection; // Binder

// Get all binders
foreach (var binder in CollectionManager.Binders)
{
    Console.WriteLine($"Binder: {binder.Name} (ID: {binder.Id})");
    Console.WriteLine($"Is Last Used: {binder.IsLastUsedBinder}, Is Wishlist: {binder.IsWishList}, Is MegaBinder: {binder.IsMegaBinder}");
    Console.WriteLine($"Item count: {binder.ItemCount}");
}

// Access items in a specific binder
var myBinder = CollectionManager.GetBinder("My Binder Name");
foreach (var card in myBinder.Items)
{
    Console.WriteLine($"{card.Name} - {card.Count}");
}
```

**Example: Accessing Decks in Your Collection**

The `CollectionManager` class abstracts collections, decks, and binders for both interactive and batch usage. Decks can be grouped by format for reporting and visualization:

```csharp
// Group all decks in the collection by their format
var decks = CollectionManager.Decks
  .GroupBy(d => d.Format)
  .Select(g => new { Format = g.Key!.Name, Decks = g.ToList() });

foreach (var format in decks)
{
  Console.WriteLine($"{format.Format} ({format.Decks.Count} decks)");
  foreach (var deck in format.Decks)
  {
    Console.WriteLine($"  '{deck.Name}' (ID: {deck.Id})");
    int mainboardCount = deck.GetRegionCount(DeckRegion.MainDeck);
    int sideboardCount = deck.GetRegionCount(DeckRegion.Sideboard);
    Console.WriteLine($"   --> {deck.ItemCount} cards ({mainboardCount} mainboard, {sideboardCount} sideboard)");
    Console.WriteLine($"  Last updated: {deck.Timestamp}");
  }
}
```

**Example: Card Lookup and Serialization**

Direct card lookup is available by name or ID, and card objects can be serialized for downstream integration:

```csharp
// Retrieve a specific card by name
var card = CollectionManager.GetCard("Black Lotus");
Console.WriteLine($"{card.Name}: {card.ManaCost}, {card.Types}, Rarity: {card.Rarity}");

// Retrieve a specific card by ID
var cardById = CollectionManager.GetCard(123456);
Console.WriteLine($"{cardById.Name}: {cardById.ManaCost}, {cardById.Types}, Rarity: {cardById.Rarity}");

// Retrieve all printings of a card
foreach (var printing in CollectionManager.GetCards("Colossal Dreadmaw"))
{
  Console.WriteLine($"Printing: {printing.SetName} ({printing.Rarity})");
}

// Serialize the card to JSON for external use
Console.WriteLine(card.ToJSON());
```

---

## Play

This section covers APIs for monitoring matches, games, tournaments, and leagues. Use these to track live events, standings, and gameplay.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.API.Play</code></td>
    <td><a href="/MTGOSDK/src/API/Play/EventManager.cs"><code>EventManager</code></a></td>
    <td>Central hub for events</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Play/Match.cs"><code>Match</code></a></td>
    <td>Represents a match</td>
  </tr>
  <tr>
    <td rowspan="1"><code>MTGOSDK.API.Play.Games</code></td>
    <td><a href="/MTGOSDK/src/API/Play/Games/Game.cs"><code>Game</code></a></td>
    <td>Represents a game</td>
  </tr>
  <tr>
    <td rowspan="1"><code>MTGOSDK.API.Play.Tournaments</code></td>
    <td><a href="/MTGOSDK/src/API/Play/Tournaments/Tournament.cs"><code>Tournament</code></a></td>
    <td>Tournament state and events</td>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.API.Play.Leagues</code></td>
    <td><a href="/MTGOSDK/src/API/Play/Leagues/League.cs"><code>League</code></a></td>
    <td>League state and events</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Play/Leagues/LeagueManager.cs"><code>LeagueManager</code></a></td>
    <td>Manages open leagues</td>
  </tr>
</table>

To work with gameplay state, import the play/event APIs:

```csharp
using MTGOSDK.API.Play;             // EventManager, Match
using MTGOSDK.API.Play.Games;       // Game
using MTGOSDK.API.Play.Tournaments; // Tournament
using MTGOSDK.API.Play.Leagues;     // League, LeagueManager
```

**Example: Accessing Events and Leagues**

The Play APIs expose live state for matches, games, tournaments, and leagues. `EventManager` acts as the central hub for querying active events and subscribing to gameplay events.

```csharp
foreach (var tournament in EventManager.FeaturedEvents)
{
  Console.WriteLine($"Tournament: {tournament.Description}");
}

foreach (var match in EventManager.JoinedEvents.OfType<Match>())
{
  Console.WriteLine($"Match: {match.Id}, State: {match.State}");
}
```

To retrieve active leagues, use the `LeagueManager`:

```csharp
foreach (var league in LeagueManager.OpenLeagues)
{
  Console.WriteLine($"League: {league.Name} (ID: {league.Id})");
  Console.WriteLine($"State: {league.LeagueState}, Score: {league.Score}/{league.MaxRounds}");
}
```

### Example: Accessing Tournament Standings

`Tournament` objects provide detailed information about current standings and round progress, which can be used to track the progress of ongoing tournaments:

```csharp
var tournament = EventManager.GetTournament(123456);
Console.WriteLine($"Tournament: {tournament.Description}");
Console.WriteLine($"Current Round: {tournament.CurrentRound}");
Console.WriteLine($"Standings:");
foreach (var player in tournament.Standings)
{
  Console.WriteLine($"  {player.Name}: {player.Record} ({player.Score} points)");
}
```

You can also access previous matches and games for each player from the tournament standings or rounds collections:

```csharp
// Per-player match history
foreach (var standing in tournament.Standings)
{
  Console.WriteLine($"Player: {standing.Player.Name}, Rank: {standing.Rank}, Record: {standing.Record}");
  // Returns a collection of MatchStandingRecord objects, which contain only
  // the match ID, round, state, result, and whether the player had a bye.
  foreach (var match in standing.PreviousMatches)
  {
    Console.WriteLine($"  Match ID: {match.Id}, Round: {match.Round}, State: {match.State}, Has Bye: {match.HasBye}");
    foreach (var game in match.GameStandingRecords)
    {
      Console.WriteLine($"    Game ID: {game.Id}, Status: {game.GameStatus}, Winner IDs: {string.Join(",", game.WinnerIds)}");
    }
  }
}
```

```csharp
// Per-round match results
foreach (var round in tournament.Rounds)
{
  Console.WriteLine($"Round {round.Number} (Complete: {round.IsComplete})");
  // Contains a collection of Match objects for this round, which relies on the
  // client to listen to match state changes as each round progresses.
  foreach (var match in round.Matches)
  {
    Console.WriteLine($"  Match ID: {match.Id}, State: {match.State}");
    foreach (var player in match.Players)
    {
      Console.WriteLine($"    Player: {player.Name}");
    }
  }
}
```

**Example: Subscribing to Events**

You can subscribe to events for real-time tracking of gameplay and tournament state changes.

```csharp
// EventHookWrapper objects can be subscribed to like regular events
match.OnGameStarted += (game) =>
{
  Console.WriteLine($"Game started: {game.Id}");
  Console.WriteLine($"Players: {string.Join(", ", game.Players.Select(p => p.Name))}");
};
match.OnGameEnded += (game) =>
{
  Console.WriteLine($"Game ended: {game.Id}");
  Console.WriteLine($"Winner: {game.Winner?.Name ?? "Draw"}");
};

// EventProxy objects also provide the same subscription model
tournament.CurrentRoundChanged += (sender, args) =>
{
  Console.WriteLine($"Tournament {tournament.Id} changed to round {args.NewRound}");
};
```

You can also clear all event subscriptions on an event or object level:

```csharp
// Clear all event subscriptions for a match or tournament
match.OnGameStarted.Clear();
tournament.CurrentRoundChanged.Clear();

// Clear all event subscriptions for all events in these objects
match.ClearEvents();
tournament.ClearEvents();
```

`Match` instances also expose additional properties and event hooks for tracking game progress, player actions, and results. Additionally, `League` and `Tournament` objects provide event streams for leaderboard changes, round transitions, and more.

### Example: Tracking Match and Game Progress

You can use the Match and Game APIs to access key properties and subscribe to important events for tracking the progress of an ongoing game:

```csharp
var match = EventManager.GetMatch(123456);
Console.WriteLine($"Match ID: {match.Id}");
Console.WriteLine($"State: {match.State}, IsComplete: {match.IsComplete}");
Console.WriteLine($"Start: {match.StartTime}, End: {match.EndTime}");
Console.WriteLine($"Players: {string.Join(", ", match.Games.SelectMany(g => g.Players.Select(p => p.Name)).Distinct())}");
Console.WriteLine($"Current Game: {match.CurrentGame?.Id}");

// Subscribe to a few match-level events
match.OnGameStarted += (game) =>
{
  Console.WriteLine($"Game started: {game.Id}");
};
match.OnGameEnded += (game) =>
{
  Console.WriteLine($"Game ended: {game.Id}");
  Console.WriteLine($"Winning players: {string.Join(", ", game.WinningPlayers.Select(p => p.Name))}");
};

// For each game in the match, access useful properties and subscribe to key events
foreach (var game in match.Games)
{
  Console.WriteLine($"Game ID: {game.Id}, Status: {game.Status}, Start: {game.StartTime}, End: {game.EndTime}");
  Console.WriteLine($"Players: {string.Join(", ", game.Players.Select(p => p.Name))}");
  Console.WriteLine($"Current Turn: {game.CurrentTurn}, Phase: {game.CurrentPhase}");
  Console.WriteLine($"Is Replay: {game.IsReplay}");
  Console.WriteLine($"Zones: {string.Join(", ", game.SharedZones.Select(z => z.Name))}");
  Console.WriteLine($"Prompt: {game.Prompt?.Text}");

  // Subscribe to a few game events
  game.OnGamePhaseChange += (phase) =>
  {
    Console.WriteLine($"Phase changed: {phase.CurrentPhase}");
  };
  game.OnLifeChange += (player) =>
  {
    Console.WriteLine($"Life changed: {player.Name} now has {player.Life} life");
  };
  game.OnZoneChange += (card) =>
  {
    Console.WriteLine($"Card zone changed: {card.Name} now in {card.Zone?.Name}");
  };
}
```

This approach allows you to monitor both static and dynamic aspects of a match and its games, including state, timing, players, zones, and selected real-time events.

---

## History

This section describes APIs for accessing completed matches and tournaments. Use these for replay analysis and statistics.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="3"><code>MTGOSDK.API.Play.History</code></td>
    <td><a href="/MTGOSDK/src/API/Play/History/HistoryManager.cs"><code>HistoryManager</code></a></td>
    <td>Loads and manages history</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Play/History/HistoricalMatch.cs"><code>HistoricalMatch</code></a></td>
    <td>Completed match data</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Play/History/HistoricalTournament.cs"><code>HistoricalTournament</code></a></td>
    <td>Completed tournament data</td>
  </tr>
</table>

To analyze game history, import the relevant history APIs:

```csharp
using MTGOSDK.API.Play.History; // HistoryManager, HistoricalMatch, HistoricalTournament
```

**Example: Querying and Iterating Game History**

Game history APIs provide access to completed matches and tournaments for a given user. This supports replay analysis of past games, win/loss tracking, and statistics.

Before querying history, verify that the history is loaded:

```csharp
if (!HistoryManager.HistoryLoaded)
{
  // This may happen if the game history file doesn't exist
  // (e.g. a new install)
  throw new OperationCanceledException("History not loaded.");
}
```

Iterate over history items (matches and tournaments) to extract old game data:

```csharp
foreach (var item in HistoryManager.Items)
{
  switch (item)
  {
    case HistoricalMatch match:
      Console.WriteLine($"Historical Match Id: {match.Id}");
      Console.WriteLine($"First Game Id: {match.GameIds.FirstOrDefault()}");
      break;
    case HistoricalTournament tournament:
      Console.WriteLine($"Historical Tournament Id: {tournament.Id}");
      Console.WriteLine($"First Match Id: {tournament.Matches.FirstOrDefault()?.Id}");
      break;
  }
}
```

---

## Chat

This section covers APIs for interacting with MTGO chat channels, messages, and users.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="4"><code>MTGOSDK.API.Chat</code></td>
    <td><a href="/MTGOSDK/src/API/Chat/ChannelManager.cs"><code>ChannelManager</code></a></td>
    <td>Manages chat channels</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Chat/Channel.cs"><code>Channel</code></a></td>
    <td>Represents a chat channel</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Chat/Message.cs"><code>Message</code></a></td>
    <td>Represents a chat message</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Chat/ChannelType.cs"><code>ChannelType</code></a></td>
    <td>Type of chat channel</td>
  </tr>
</table>

To use chat APIs, import the chat namespace:

```csharp
using MTGOSDK.API.Chat; // ChannelManager, Channel, Message, ChannelType
```

**Example: Enumerating Channels and Sending Messages**

Channels in MTGO represent different chat rooms, including system, private, and game-specific channels. These are also used in games to track chat logs as well as game logs. To enumerate channels and access their properties:

```csharp
foreach (var channel in ChannelManager.Channels)
{
  Console.WriteLine($"{channel.Name} ({channel.Type})");
}

channel.ChatSession?.Send("Hello!");
foreach (var msg in channel.Messages)
{
  string user = msg.User != null ? msg.User.Name : "<system>";
  Console.WriteLine($"[{msg.Timestamp}] {user}: {msg.Text}");
}
```

You can also select channels by name or ID, and inspect chat session details or user lists:

```csharp
var mainLobby = ChannelManager.GetChannel("Main Lobby");
Console.WriteLine($"Channel: {mainLobby.Name}, Users: {mainLobby.UserCount}");
```

---

## Users

This section describes APIs for managing user profiles, buddy lists, and social connections.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.API.Users</code></td>
    <td><a href="/MTGOSDK/src/API/Users/UserManager.cs"><code>UserManager</code></a></td>
    <td>Manages users and buddies</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Users/User.cs"><code>User</code></a></td>
    <td>Represents a user</td>
  </tr>
</table>

To interact with user profiles and buddy lists, import the users namespace:

```csharp
using MTGOSDK.API.Users; // UserManager, User
```

**Example: User Lookup and Buddy List**

User objects encapsulate identity, login state, avatar, and social connections. Retrieve a user by ID or name:

```csharp
var user = UserManager.GetUser(3136075, "VidereBot1");
Console.WriteLine($"User: {user.Name} (ID: {user.Id})");

if (user.IsBuddy)
{
  Console.WriteLine("User is a buddy.");
}
```

Enumerate the current user's buddy list:

```csharp
foreach (var buddy in UserManager.GetBuddyUsers())
{
  Console.WriteLine($"Buddy: {buddy.Name} (ID: {buddy.Id})");
}
```

User objects also expose additional state such as login status, guest status, and avatar information.

---

## Trade

This section covers APIs for accessing trade posts, trade partners, and managing trades in MTGO.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="4"><code>MTGOSDK.API.Trade</code></td>
    <td><a href="/MTGOSDK/src/API/Trade/TradeManager.cs"><code>TradeManager</code></a></td>
    <td>Central manager for trade posts, partners, and current trades</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Trade/TradePost.cs"><code>TradePost</code></a></td>
    <td>Represents a trade post in the marketplace</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Trade/TradePartner.cs"><code>TradePartner</code></a></td>
    <td>Represents a previous trade partner</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Trade/TradeEscrow.cs"><code>TradeEscrow</code></a></td>
    <td>Represents an ongoing trade escrow</td>
  </tr>
</table>

To use trade APIs, import the trade namespace:

```csharp
using MTGOSDK.API.Trade; // TradeManager, TradePost, TradePartner, TradeEscrow
```

**Example: Accessing Trade Posts and Partners**

```csharp
// List recent trade posts
foreach (var post in TradeManager.AllPosts.Take(5))
{
    Console.WriteLine($"Post by {post.Poster.Name}: {post.Message}");
    foreach (var wanted in post.Wanted)
        Console.WriteLine($"  Wants: {wanted.Quantity}x {wanted.Card.Name}");
    foreach (var offered in post.Offered)
        Console.WriteLine($"  Offers: {offered.Quantity}x {offered.Card.Name}");
}

// Your own post
var myPost = TradeManager.MyPost;
if (myPost != null)
    Console.WriteLine($"My post: {myPost.Message}");

// List previous trade partners
foreach (var partner in TradeManager.TradePartners)
{
    Console.WriteLine($"Traded with: {partner.Poster.Name} at {partner.LastTradeTime}");
}
```

**Example: Accessing Current Trade Escrow**

```csharp
var currentTrade = TradeManager.CurrentTrade;
if (currentTrade != null)
{
    Console.WriteLine($"Trade with: {currentTrade.TradePartner.Name}");
    Console.WriteLine($"State: {currentTrade.State}, Accepted: {currentTrade.IsAccepted}");
    foreach (var item in currentTrade.TradedItems)
        Console.WriteLine($"You traded: {item.Quantity}x {item.Card.Name}");
    foreach (var item in currentTrade.PartnerTradedItems)
        Console.WriteLine($"Partner traded: {item.Quantity}x {item.Card.Name}");
}
```

---

## Settings

This section covers APIs for reading and reporting MTGO client configuration and preferences.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.API.Settings</code></td>
    <td><a href="/MTGOSDK/src/API/Settings/SettingsService.cs"><code>SettingsService</code></a></td>
    <td>Reads client settings</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Settings/Setting.cs"><code>Setting</code></a></td>
    <td>Setting keys and options</td>
  </tr>
</table>

For client settings, import the settings namespace:

```csharp
using MTGOSDK.API.Settings; // SettingsService, Setting
```

**Example: Reading Client Settings**

Settings can be accessed to read MTGO client configuration, including UI preferences, last logged-in user, and other options. To retrieve a setting value:

```csharp
var lastUser = SettingsService.GetSetting(Setting.LastLoginName);
Console.WriteLine($"Last login user: {lastUser}");

var showBigCard = SettingsService.GetSetting<bool>(Setting.ShowBigCardWindow);
Console.WriteLine($"Show big card window: {showBigCard}");

var phaseLadderDefault = SettingsService.GetDefaultSetting<bool>(Setting.ShowPhaseLadder);
Console.WriteLine($"Default: Show phase ladder: {phaseLadderDefault}");
```

Use these APIs to read user and application settings for diagnostics or configuration reporting. This is also a good place to look for how the client panes and windows are sized (for instance, in the DuelScene), which can help with aligning GUIs ontop.

---

## Toasts and Dialogs

This section describes APIs for displaying notifications and modal dialogs in the MTGO client.

**Key Classes:**
<table>
  <tr>
    <th>Namespace</th>
    <th>Class</th>
    <th>Description</th>
  </tr>
  <tr>
    <td rowspan="2"><code>MTGOSDK.API.Interface</code></td>
    <td><a href="/MTGOSDK/src/API/Interface/ToastViewManager.cs"><code>ToastViewManager</code></a></td>
    <td>Shows toast notifications</td>
  </tr>
  <tr>
    <td><a href="/MTGOSDK/src/API/Interface/DialogService.cs"><code>DialogService</code></a></td>
    <td>Shows modal dialogs</td>
  </tr>
</table>

To display toast notifications and modal dialogs in the MTGO client, import the interface APIs:

```csharp
using MTGOSDK.API.Interface; // ToastViewManager, DialogService
```

### Example: Showing a Toast Notification

Toast notifications provide brief, non-blocking feedback to users:

```csharp
// Show a simple toast notification
ToastViewManager.ShowToast("Game Update", "You have joined a new match.");
```

### Example: Toast with Navigation

A toast can also be configured to navigate to a specific event when clicked:

```csharp
// Show a toast that navigates the client to a tournament event
var tournamentEvent = EventManager.GetTournamentEvent(123456);
ToastViewManager.ShowToast("Tournament Started", "Click to view tournament.", tournamentEvent);
```

### Example: Showing a Modal Dialog

To display a modal dialog that requires user confirmation or provides important information:

```csharp
// Show a confirmation dialog
bool result = DialogService.ShowModal(
  "Confirm Action",
  "Are you sure you want to leave the league?",
  okButton: "Leave",
  cancelButton: "Stay"
);

// Handle the user's response
if (result)
{
  Console.WriteLine("User confirmed leaving the league.");
}
else
{
  Console.WriteLine("User cancelled.");
}
```

These APIs enable applications to provide feedback and prompt the user in a way that integrates with MTGO's native UI.

---

MTGOSDK provides a consistent, type-safe interface to MTGO, suitable for automation, analytics, and user-facing tools. The APIs are designed to be intuitive and easy to use, allowing developers to focus on building new features for MTGO.

For installation, build instructions, and architectural details, refer to the [README](README.md) and [architecture documentation](docs/architecture/core-classes.md)