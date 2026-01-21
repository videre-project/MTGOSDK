# Collection Guide

This guide covers the Collection APIs for accessing decks, cards, and binders. These are the APIs you'll use most often when building deck management tools, collection exporters, or analytics dashboards.

## Overview

MTGO organizes your cards into three main concepts:

- **Collection**: All cards you own, viewable in the Collection scene
- **Decks**: Named lists of cards with mainboard and sideboard regions
- **Binders**: Custom groupings like wishlists or mega binders

The `CollectionManager` class is the central access point for all of these. It provides static properties and methods to query your collection without needing to create any instances. The collection data is loaded when the user logs in and stays in sync with MTGO's state as you add, remove, or trade cards.

```csharp
using MTGOSDK.API.Collection;
```

---

## Accessing Your Collection

The full collection is available through `CollectionManager.Collection`. Since MTGO collections can contain thousands of unique cards, the SDK provides a `GetFrozenCollection` method that creates a compact, read-only snapshot optimized for iteration:

```csharp
var collection = CollectionManager.Collection
  ?? throw new InvalidOperationException("Collection not loaded.");

var snapshot = collection.GetFrozenCollection.ToArray();
Console.WriteLine($"You own {collection.ItemCount} cards");

foreach (var card in snapshot.Take(10))
{
  Console.WriteLine($"{card.Quantity}x {card.Name}");
}
```

The `Collection` property can be null if the user hasn't logged in yet or the collection hasn't finished loading. Always check for null before accessing it "in production code. The `ItemCount` property gives you the total number of cards (counting duplicates), while iterating the frozen collection gives you unique cards with quantities.

The frozen collection creates an immutable snapshot at the moment you call it. This is useful for exports or analytics where you want consistent data even if the collection changes during iteration. For live displays that should update as cards are added or removed, use the regular `Items` property instead.

---

## Working with Decks

Decks are the most commonly accessed collection type. You can enumerate all decks, filter by format, or look up specific decks by name or ID.

### Listing Decks

```csharp
foreach (var deck in CollectionManager.Decks)
{
  Console.WriteLine($"{deck.Name} ({deck.Format?.Name})");
  Console.WriteLine($"  {deck.ItemCount} cards total");
  Console.WriteLine($"  Last modified: {deck.Timestamp}");
}
```

The `Decks` collection contains all decks the user has created. Each deck has a `Name`, an optional `Format` (which can be null for unassigned decks), an `ItemCount` showing total cards, and a `Timestamp` showing when it was last modified. The timestamp is useful for sorting decks by recent activity.

### Grouping by Format

```csharp
var decksByFormat = CollectionManager.Decks
  .GroupBy(d => d.Format?.Name ?? "No Format")
  .OrderBy(g => g.Key);

foreach (var group in decksByFormat)
{
  Console.WriteLine($"{group.Key}: {group.Count()} decks");
}
```

Format-based grouping is useful for deck browsers or format-specific tools. The `Format` property can be null for decks that haven't been assigned a format, so we use the null-coalescing operator to handle those cases with a "No Format" label.

### Deck Regions

Each deck has a mainboard and sideboard. Use `GetRegionCount` to get card counts for each region:

```csharp
var deck = CollectionManager.Decks.First();
int mainboard = deck.GetRegionCount(DeckRegion.MainDeck);
int sideboard = deck.GetRegionCount(DeckRegion.Sideboard);

Console.WriteLine($"{deck.Name}: {mainboard} main, {sideboard} side");
```

The `DeckRegion` enum includes `MainDeck`, `Sideboard`, and `CommandZone` for Commander decks. The count represents individual cards, not unique cards, so four copies of Lightning Bolt count as 4.

### Accessing Cards by Region

To get the actual cards in a region, use `GetCards`. This returns `CardQuantityPair` objects that pair each card with its quantity in that region:

```csharp
var deck = CollectionManager.Decks.First();

Console.WriteLine("Mainboard:");
foreach (var pair in deck.GetCards(DeckRegion.MainDeck))
{
  Console.WriteLine($"  {pair.Quantity}x {pair.Card.Name}");
}

Console.WriteLine("Sideboard:");
foreach (var pair in deck.GetCards(DeckRegion.Sideboard))
{
  Console.WriteLine($"  {pair.Quantity}x {pair.Card.Name}");
}
```

The `CardQuantityPair` type has two key properties: `Card` (the card definition with name, mana cost, etc.) and `Quantity` (how many copies are in this region). This is the same type used when creating new decks programmatically.

---

## Creating New Decks

The `Deck` class provides constructors for creating new decks programmatically. You'll need to build lists of `CardQuantityPair` objects for the mainboard and sideboard.

The most efficient way to create `CardQuantityPair` objects is with the name and optional catalog ID constructor, which avoids IPC calls to look up cards:

```csharp
var mainboard = new List<CardQuantityPair>
{
  new("Lightning Bolt", 4, catalogId: 37240),
  new("Mountain", 20),
};

var sideboard = new List<CardQuantityPair>
{
  new(catalogId: 60944, quantity: 3), // Pyroblast
};

var deck = new Deck(
  mainboard,
  sideboard,
  name: "Mono Red Burn",
  format: PlayFormat.Legacy
);

Console.WriteLine($"Created: {deck.Name}");
Console.WriteLine($"Format: {deck.Format?.Name}");
```

You can provide just the card name (the SDK will look up the ID when needed), just the catalog ID (for maximum efficiency), or both (name for readability, ID for performance). The catalog ID is the unique identifier for a specific card printing in MTGO's database.

These deck objects don't appear in the collection scene of the MTGO client by default. They exist in memory and can be used for operations like matchmaking or export, but they're not persisted unless you explicitly save them.

---

## Working with Binders

Binders are custom groupings in your collection. MTGO has several built-in binder types (wishlist, mega binder) and supports user-created binders for organizing cards.

```csharp
foreach (var binder in CollectionManager.Binders)
{
  Console.WriteLine($"{binder.Name} ({binder.ItemCount} cards)");
  
  if (binder.IsWishList)
    Console.WriteLine("  (Wishlist)");
  if (binder.IsMegaBinder)
    Console.WriteLine("  (Mega Binder)");
}
```

The boolean properties `IsWishList` and `IsMegaBinder` identify special binder types. The wishlist is used by MTGO's want-list feature for trading, while mega binders are large collections used by some trading tools. Regular binders created by users won't have either flag set.

To access cards in a specific binder:

```csharp
var binder = CollectionManager.GetBinder("My Trade Binder");
foreach (var card in binder.Items.Take(10))
{
  Console.WriteLine($"  {card.Count}x {card.Name}");
}
```

The `GetBinder` method searches by name. If no binder with that name exists, you'll get null back. The `Items` collection contains the cards in the binder, which you can iterate, filter, or export just like deck contents.

---

## Card Lookup

You can look up individual cards by name or catalog ID. The SDK searches MTGO's internal card database, which contains every card ever printed on MTGO.

### By Name

```csharp
var card = CollectionManager.GetCard("Black Lotus");
Console.WriteLine($"{card.Name}");
Console.WriteLine($"  Mana cost: {card.ManaCost}");
Console.WriteLine($"  Types: {card.Types}");
Console.WriteLine($"  Set: {card.SetName}");
Console.WriteLine($"  Rarity: {card.Rarity}");
```

Name lookup returns the first matching card. If multiple printings exist (different sets, different art), you'll get one of them, but the exact one isn't guaranteed. For specific printings, use the catalog ID or `GetCards` to enumerate all versions.

### By Catalog ID

```csharp
var card = CollectionManager.GetCard(123456);
Console.WriteLine($"{card.Name} (ID: {card.Id})");
```

Catalog ID lookup is faster and unambiguous since each ID maps to exactly one printing. Use this when you know the specific card version you want, such as when deserializing saved data.

### Multiple Printings

Many cards have multiple printings across different sets. Use `GetCards` to retrieve all versions:

```csharp
foreach (var printing in CollectionManager.GetCards("Colossal Dreadmaw"))
{
  Console.WriteLine($"{printing.SetName} ({printing.Rarity})");
}
```

This returns every printing of the named card in MTGO's database. Each printing is a separate `Card` object with its own catalog ID, set name, art, and potentially different rarity (if the card was shifted between printings).

---

## Batch Serialization

When working with large collections or decks, accessing properties one at a time can be slow because each property access requires an IPC call to the MTGO process. The `SerializeItemsAs<T>` method fetches all properties for all items in a single batch call.

You can define a custom interface that specifies only the properties you need. The interface property names must match the `Card` wrapper class properties:

```csharp
// Define an interface with just the properties you need
public interface ICardSortData
{
  string Name { get; }
  int ConvertedManaCost { get; }
  string Rarity { get; }
}

var deck = CollectionManager.Decks.First();

// Batch fetch only the properties defined in our interface
var cards = deck.SerializeItemsAs<ICardSortData>().ToList();

// Now we can sort and filter without additional IPC calls
var sorted = cards
  .OrderBy(c => c.ConvertedManaCost)
  .ThenBy(c => c.Name);

foreach (var card in sorted)
{
  Console.WriteLine($"{card.Name} (CMC: {card.ConvertedManaCost})");
}
```

The batch call fetches all specified properties for all cards in a single round-trip to the MTGO process. This eliminates the per-property IPC overhead that makes naive iteration slow. By defining a minimal interface, you reduce the amount of data transferred and avoid fetching properties you don't need.

This approach can be 5-10x faster than accessing properties individually, especially for large decks or when you need to access multiple properties per card (like sorting by mana cost and then by name).

---

## JSON Serialization

Card objects support JSON serialization for export to external tools:

```csharp
var card = CollectionManager.GetCard("Black Lotus");
string json = card.ToJSON();
Console.WriteLine(json);
```

This produces a JSON representation of the card's properties that can be stored, sent to other applications, or used for interoperability with deck-building websites and other MTGO tools. The JSON includes all public properties from the `Card` wrapper class.

---

## Next Steps

- [Play Guide](./play.md) - Matches, tournaments, and leagues
- [Games Guide](./games.md) - In-game state tracking
- [Trade Guide](./trade.md) - Marketplace and trading
