# Getting Started with MTGOSDK

This guide walks through setting up MTGOSDK and connecting to the MTGO client. By the end, you'll understand the different ways to initialize the SDK depending on your use case.

## Prerequisites

- **.NET 10 SDK** (or newer): [Download .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Visual Studio 2026 v18.0+** or any compatible .NET IDE
- Access to the MTGO client (running or installed)

## Installation

Install MTGOSDK via NuGet:

```powershell
dotnet add package MTGOSDK
```

Or using the Package Manager Console:

```powershell
Install-Package MTGOSDK
```

For the latest development builds, see [building from source](../README.md#building-this-project).

---

## Connecting to MTGO

There are two main approaches to connect your application to MTGO:

1. **Explicit initialization** – You create a `Client` instance and control the full lifecycle
2. **Implicit initialization** – You access API classes directly and the SDK handles connection automatically

Which you choose depends on whether you need to launch MTGO, handle login, or just read data from an already-running session.

### Explicit Client Initialization

When you need full control over the MTGO process (launching it, logging in, and cleaning up when you're done), use the `Client` class directly:

```csharp
using MTGOSDK.API;

using var client = new Client(new ClientOptions
{
  CreateProcess = true,
  CloseOnExit = true,
  AcceptEULAPrompt = true
});

Console.WriteLine($"Connected to MTGO v{Client.Version}");

if (!client.IsConnected)
{
  await client.LogOn("username", password);
  Console.WriteLine($"Logged in as {client.CurrentUser.Name}");
}

client.IsConnectedChanged += delegate(object? sender)
{
  Console.WriteLine("MTGO client disconnected.");
};

// Your application logic here...

await client.LogOff();
```

This code starts MTGO if it isn't already running, waits for initialization, then logs in with the provided credentials. The `using` statement ensures the client is properly disposed when your code finishes. Since we set `CloseOnExit = true`, MTGO will also be terminated.

The `IsConnectedChanged` event fires if the user logs out or MTGO crashes unexpectedly. You can also uses the `Disposed` event from `MTGOSDK.Core.Remoting.RemoteClient` to detect if the MTGO process has been terminated by the user or an external process. For long-running bots, you'll want to handle this to restart or exit gracefully.

This pattern is used in the [BasicBot example](../examples/BasicBot/Program.cs), which also demonstrates checking server status before attempting to connect.

### Implicit Initialization

If MTGO is already running and the user is logged in, you don't need a `Client` at all. Just import the namespace you need and start using it:

```csharp
using MTGOSDK.API.Play.History;

// This will implicitly initialize the SDK's connection to MTGO.
if (!HistoryManager.HistoryLoaded)
  throw new OperationCanceledException("History not loaded.");

foreach (var item in HistoryManager.Items)
{
  Console.WriteLine($"Event ID: {item.Id}");
}
```

The first time you access any SDK type like `HistoryManager`, the SDK automatically locates the running MTGO process and establishes a connection. There's no explicit setup required.

This approach works well for companion tools, overlays, or quick scripts where you're reading data from an existing session. The [GameTracker example](../examples/GameTracker/Program.cs) uses this pattern to iterate through match history without any client setup code.

One thing to keep in mind: if MTGO isn't running when you access an SDK type, you'll get an error. The SDK won't wait for a process to appear. This is one reason you might prefer explicit `Client` initialization even for read-only tools (as we do in the [Tracker project](https://github.com/videre-project/Tracker)), since it gives you control over process discovery and startup. Similarly, some APIs like `HistoryManager` won't have data until the user completes the login process.

### Attaching to a Running Session

Sometimes you want the benefits of an explicit `Client` instance, like handling disconnect events, but MTGO is already running and you don't want to restart it:

```csharp
using MTGOSDK.API;

using var client = new Client(new ClientOptions());

Console.WriteLine($"Current user: {client.CurrentUser.Name}");
```

With default options, `Client` attaches to whatever MTGO process is running. You get the same event hooks and lifecycle management without launching a new instance. This is useful for tools that need to react to connection changes but shouldn't control MTGO's lifecycle.

---

## ClientOptions Reference

The `ClientOptions` struct controls how the SDK connects to MTGO:

| Option | Default | Description |
|--------|---------|-------------|
| `CreateProcess` | `false` | Launch a new MTGO process (kills existing instances first) |
| `StartMinimized` | `false` | Start MTGO minimized to the taskbar |
| `CloseOnExit` | `false` | Kill MTGO when the `Client` is disposed |
| `AcceptEULAPrompt` | `false` | Automatically accept the EULA dialog on launch |
| `UseDaybreakAPI` | `true` | Use Daybreak APIs for server status checks |

For bot automation, you typically want all the lifecycle options enabled. For interactive development, defaults work fine since you're attaching to your own running client.

---

## Checking Server Status

Before attempting to launch MTGO, you can check whether the servers are online:

```csharp
using MTGOSDK.API;

while (!await Client.IsOnline())
{
  Console.WriteLine("MTGO servers are offline. Waiting...");
  await Task.Delay(TimeSpan.FromMinutes(30));
}

using var client = new Client(new ClientOptions { CreateProcess = true });
```

This is particularly useful for bots that run unattended. If you try to log in while servers are down, you'll get an error. Checking first lets you wait gracefully and retry later.

It's worth noting here that the `CreateProcess` option will also manage updating MTGO if needed after downtime. We use a custom installer (located in `MTGOSDK/lib/Launcher`) that manages the update process and ClickOnce service registration without any user intervention needed.

---

## Quick Examples

Here are a few common tasks to get you started. Each of these works with either explicit or implicit initialization.

### Reading Your Decks

```csharp
using MTGOSDK.API.Collection;

foreach (var deck in CollectionManager.Decks)
{
  Console.WriteLine($"{deck.Name} ({deck.Format?.Name})");
  Console.WriteLine($"  Cards: {deck.ItemCount}");
}
```

`CollectionManager` provides access to all decks, binders, and cards in the user's collection. Decks are sorted by format and include card counts, timestamps, and full card lists.

### Viewing Featured Tournaments

```csharp
using MTGOSDK.API.Play;

foreach (var tournament in EventManager.FeaturedEvents)
{
  Console.WriteLine($"{tournament.Description} - {tournament.TotalPlayers} players");
}
```

`EventManager` exposes both featured events (visible in the play lobby) and events the user has joined. You can subscribe to state changes for real-time updates during tournaments.

### Looking Up Card Data

```csharp
using MTGOSDK.API.Collection;

var card = CollectionManager.GetCard("Black Lotus");
Console.WriteLine($"{card.Name} - {card.ManaCost}");
Console.WriteLine($"Set: {card.SetName}, Rarity: {card.Rarity}");
```

Card lookup works by name or catalog ID. The returned `Card` object includes all printings, artwork references, and metadata from MTGO's internal card database stored in-memory.

---

## Next Steps

- **[Guides](./guides/README.md)** – Task-oriented tutorials for all API features
- **[Examples](../examples)** – Working sample applications
- **[Architecture Guide](./architecture/README.md)** – How the SDK works internally
- **[FAQ](./FAQ.md)** – Common questions and troubleshooting

---

## Troubleshooting

If the SDK hangs on startup, make sure MTGO is running using `CreateProcess = true` to have the SDK launch it. Login failures usually mean invalid credentials or offline servers (which you can check using `Client.IsOnline()`).

For APIs like `HistoryManager` or `EventManager`, data isn't available until the user is fully logged in and MTGO has finished loading. If you see "History not loaded" or "Events not loaded" errors, give it a moment after login. You can use the `Client.WaitForClientReady()` method to wait for this.

Build errors typically come from .NET SDK version mismatches or NuGet package restore failures. MTGOSDK requires .NET 10 or newer.

For additional help, see [GitHub Issues](https://github.com/videre-project/MTGOSDK/issues).
