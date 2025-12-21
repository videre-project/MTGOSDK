/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Graphics;
using MTGOSDK.Core.Reflection.Serialization;

var client = new MTGOSDK.API.Client();

var start = DateTime.Now;
var blackLotus1 = CollectionManager.GetCard("Marsh Flats");
var art = await CardRenderer.GetCardArtPath(blackLotus1);
Console.WriteLine(art);
TimeSpan elapsed = DateTime.Now - start;
Console.WriteLine($"GetCardArtPath took {elapsed.TotalMilliseconds} ms.");


// // Time how long it takes to initialize the CollectionManager
// DateTime initStart = DateTime.Now;
// if (CollectionManager.CardNameToIds.Count == 0) { }
// TimeSpan initElapsed = DateTime.Now - initStart;
// Console.WriteLine($"CollectionManager initialization took {initElapsed.TotalMilliseconds} ms.\n");

// // Create a list of <PlayFormat, List<Deck>> pairs.
// var decks = CollectionManager.Decks
//   .GroupBy(d => d.Format)
//   .Select(g => new { Format = g.Key!.Name, Decks = g.ToList() })
//   .ToList();
// foreach (var format in decks)
// {
//   Console.WriteLine($"{format.Format} ({format.Decks.Count} decks)");
//   foreach (var deck in format.Decks)
//   {
//     Console.WriteLine($"  '{deck.Name}' (ID: {deck.Id})");
//     int mainboardCount = deck.GetRegionCount(DeckRegion.MainDeck);
//     int sideboardCount = deck.GetRegionCount(DeckRegion.Sideboard);
//     Console.WriteLine($"   --> {deck.ItemCount} cards ({mainboardCount} mainboard, {sideboardCount} sideboard)");
//     Console.WriteLine($"  Last updated: {deck.Timestamp}");
//   }
// }

// // Retrieves the main collection grouping from the CollectionManager.
// CardGrouping<Collection> collection = CollectionManager.Collection
//   ?? throw new InvalidOperationException("Collection not loaded.");

// Console.WriteLine($"\nCollection ({collection.ItemCount} items)");
// Console.WriteLine($"Last updated: {collection.Timestamp}");

// //
// // Here we extract a snapshot of the collection.
// //
// // This works by requesting a dump of the debugData from the collection grouping,
// // which we use to parse and create a local collection of card objects.
// // This will work for any collection size and is typically very fast even for
// // collections with hundrends of thousands of unique items.
// //
// DateTime start = DateTime.Now;
// CardQuantityPair[] frozenCollection = collection.GetFrozenCollection.ToArray();
// TimeSpan elapsed = DateTime.Now - start;
// Console.WriteLine($"\nGetFrozenCollection took {elapsed.TotalMilliseconds} ms to retrieve {frozenCollection.Length} items.\n");

// foreach (CardQuantityPair card in frozenCollection.Take(25))
// {
//   Console.WriteLine($"{card.Quantity}x {card.Name} ({card.Id})");
// }
// Console.WriteLine($"...and {frozenCollection.Length - 25} more.");

// // Select a random card from the collection
// CardQuantityPair randomCard = frozenCollection[Random.Shared.Next(frozenCollection.Length)];
// Console.WriteLine($"\nRandom card: {randomCard.Quantity}x {randomCard.Name} ({randomCard.Id})");
// Console.WriteLine($"{randomCard.Card.ToJSON()}"); // Can print as a JSON object
