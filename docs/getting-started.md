# Getting Started with MTGOSDK

This guide will help you quickly set up and use MTGOSDK to interact with the Magic: The Gathering Online (MTGO) client.

## Prerequisites

- **.NET 10 SDK** (or newer): [Download .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Visual Studio 2022 v17.14+** or any compatible .NET IDE
- Access to the MTGO client (running or installed, for live testing)

## Installation

You can install MTGOSDK in your project via NuGet:

```powershell
dotnet add package MTGOSDK
```

Or, using the NuGet Package Manager in Visual Studio:

```powershell
Install-Package MTGOSDK
```

For advanced integration or the latest features, consider [building from source](../README.md#building-this-project) and using the local package feed.

## Basic Usage

Start by importing the SDK in your C# project:

```csharp
using MTGOSDK.API; // Core client APIs
```

### Example: Connecting and Logging In

```csharp
// Create a new client instance to automate launching and logging in.
using var client = new Client(new ClientOptions
{
  CreateProcess = true,
  CloseOnExit = true,
  AcceptEULAPrompt = true
});

// Log in using your MTGO credentials
await client.LogOn("username", password);

// Check current user
Console.WriteLine($"Logged in as: {client.CurrentUser.Name}");

// Always log off and dispose when done
await client.LogOff();
```

### Example: Reading Your Collection

```csharp
using MTGOSDK.API.Collection; // CollectionManager, Deck

// Print all deck names
foreach (var deck in CollectionManager.Decks)
{
  Console.WriteLine(deck.Name);
}

// Get a list of cards from a specific deck
var myDeck = CollectionManager.GetDeck("My Favorite Deck");
foreach (var card in myDeck.GetCards(DeckRegion.MainDeck))
{
  Console.WriteLine($"{card.Name} - {card.Count}");
}
```

### Example: Monitoring Games and Events

```csharp
using MTGOSDK.API.Play; // EventManager

foreach (var match in EventManager.JoinedEvents.OfType<Match>())
{
  Console.WriteLine($"Match: {match.Id}, State: {match.State}");
}
```

## Next Steps

- Explore the [API Reference](./api-reference.md) for more advanced features.
- See [examples](../examples) for practical sample applications.
- Review the [Architecture Guide](./architecture/README.md) for a deeper understanding of the SDK's design.

## Troubleshooting

- Ensure your .NET version matches the SDK requirements.
- If you experience issues connecting to MTGO, confirm the client is running and your credentials are valid.
- For local builds, verify your NuGet configuration includes the SDK's local package feed.

## Community & Support

- Questions? Check the [FAQ](./FAQ.md).
- Report issues or request features on [GitHub Issues](https://github.com/videre-project/MTGOSDK/issues).

---

For further documentation, visit the [docs](./README.md) index.