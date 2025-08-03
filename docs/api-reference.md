## API Structure & Usage

MTGOSDK organizes its APIs by feature domain, providing direct access to MTGO session management, card collections, gameplay state, chat, user profiles, and client settings. These APIs support automation, structured data retrieval, and real-time event responses.

### Session Management and Entry Points

To work with sessions, import the core client types:

```csharp
using MTGOSDK.API; // Client, ClientOptions
```

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

### Accessing Collections

To access collection features, import the collection API:

```csharp
using MTGOSDK.API.Collection; // CollectionManager, Card, Deck, Binder
```

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

### Monitoring Live Gameplay and Events

To work with gameplay state, import the play/event APIs:

```csharp
using MTGOSDK.API.Play;             // EventManager, Match
using MTGOSDK.API.Play.Games;       // Game
using MTGOSDK.API.Play.Tournaments; // Tournament
using MTGOSDK.API.Play.Leagues;     // League, LeagueManager
```

The Play APIs expose live state for matches, games, tournaments, and leagues. `EventManager` acts as the central hub for querying active events and subscribing to gameplay events.

To inspect featured tournaments or enumerate current events:

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

You can also subscribe to gameplay events for real-time monitoring and automation:

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

However, you can also clear all event subscriptions on an event or object level:

```csharp
// Clear all event subscriptions for a match or tournament
match.OnGameStarted.Clear();
tournament.CurrentRoundChanged.Clear();

// Clear all event subscriptions for all events in these objects
match.ClearEvents();
tournament.ClearEvents();
```

`Match` instances also expose additional properties and event hooks for tracking game progress, player actions, and results. Additionally, `League` and `Tournament` objects provide event streams for leaderboard changes, round transitions, and more.

---

### Accessing Game History

To analyze game history, import the relevant history APIs:

```csharp
using MTGOSDK.API.Play.History; // HistoryManager, HistoricalMatch, HistoricalTournament
```

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

### Chat

To use chat APIs, import the chat namespace:

```csharp
using MTGOSDK.API.Chat; // ChannelManager, Channel, Message, ChannelType
```

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

### Users

To interact with user profiles and buddy lists, import the users namespace:

```csharp
using MTGOSDK.API.Users; // UserManager, User
```

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

### Settings

For client settings, import the settings namespace:

```csharp
using MTGOSDK.API.Settings; // SettingsService, Setting
```

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

### Toasts and Dialogs

To display toast notifications and modal dialogs in the MTGO client, import the interface APIs:

```csharp
using MTGOSDK.API.Interface; // ToastViewManager, DialogService
```

Toast notifications provide brief, non-blocking feedback to users:

```csharp
// Show a simple toast notification
ToastViewManager.ShowToast("Game Update", "You have joined a new match.");
```

A toast can also be configured to navigate to a specific event when clicked:

```csharp
// Show a toast that navigates the client to a tournament event
var tournamentEvent = EventManager.GetTournamentEvent(123456);
ToastViewManager.ShowToast("Tournament Started", "Click to view tournament.", tournamentEvent);
```

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

These APIs enable applications to provide feedback and prompt the user for choices in a way that integrates with MTGO's native UI.

---

MTGOSDK provides a consistent, type-safe interface to MTGO, suitable for automation, analytics, and user-facing tools. The APIs are designed to be intuitive and easy to use, allowing developers to focus on building new features for MTGO.

For installation, build instructions, and architectural details, refer to the [README](README.md) and [architecture documentation](docs/architecture/core-classes.md).