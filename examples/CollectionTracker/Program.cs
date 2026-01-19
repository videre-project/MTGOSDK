/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection.Serialization;


// Create a list of <PlayFormat, List<Deck>> pairs.
var decks = CollectionManager.Decks
  .GroupBy(d => d.Format)
  .Select(g => new { Format = g.Key!.Name, Decks = g.ToList() })
  .ToList();
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

// Retrieves the main collection grouping from the CollectionManager.
CardGrouping<Collection> collection = CollectionManager.Collection
  ?? throw new InvalidOperationException("Collection not loaded.");

Console.WriteLine($"\nCollection ({collection.ItemCount} items)");
Console.WriteLine($"Last updated: {collection.Timestamp}");

//
// Here we extract a snapshot of the collection.
//
// This works by requesting a dump of the debugData from the collection grouping,
// which we use to parse and create a local collection of card objects.
// This will work for any collection size and is typically very fast even for
// collections with hundrends of thousands of unique items.
//
DateTime start = DateTime.Now;
CardQuantityPair[] frozenCollection = collection.GetFrozenCollection.ToArray();
TimeSpan elapsed = DateTime.Now - start;
Console.WriteLine($"\nGetFrozenCollection took {elapsed.TotalMilliseconds} ms to retrieve {frozenCollection.Length} items.\n");

foreach (CardQuantityPair card in frozenCollection.Take(25))
{
  Console.WriteLine($"{card.Quantity}x {card.Name} ({card.Id})");
}
Console.WriteLine($"...and {frozenCollection.Length - 25} more.");

// Select a random card from the collection
CardQuantityPair randomCardA = frozenCollection[Random.Shared.Next(frozenCollection.Length)];
CardQuantityPair randomCardB = frozenCollection[Random.Shared.Next(frozenCollection.Length)];
Console.WriteLine($"\nRandom card A: {randomCardA.Quantity}x {randomCardA.Name} ({randomCardA.Id})");
Console.WriteLine($"{randomCardA.Card.ToJSON()}");
Console.WriteLine($"Random card B: {randomCardB.Quantity}x {randomCardB.Name} ({randomCardB.Id})");
Console.WriteLine($"{randomCardB.Card.ToJSON()}");

// Try creating a new deck with both cards:
Deck newDeck = new Deck(
  [new CardQuantityPair(randomCardA.Id, 4)],
  [new CardQuantityPair(randomCardB.Id, 4)]);
Console.WriteLine($"\nNew deck: {newDeck.ItemCount} cards");
Console.WriteLine($"{newDeck.ToJSON()}");
