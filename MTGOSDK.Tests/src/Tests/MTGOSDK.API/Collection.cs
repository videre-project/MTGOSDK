/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class Collection : CollectionValidationFixture
{
  [Test]
  public void Test_Collection()
  {
    var collection = CollectionManager.Collection;
    ValidateCollection(collection);

    // TODO: Get an ItemCollection instance of the collection in the heap
    //       created by the `ActiveTradeViewModel.SyncCollection` method.
  }

  [Test]
  public void Test_Binders()
  {
    var binder = CollectionManager.Binders.First();
    ValidateBinder(binder);
    ValidateBinder(CollectionManager.GetBinder(binder.Id));
    if (CollectionManager.LastUsedBinder is { } lastUsedBinder)
      ValidateBinder(lastUsedBinder);
    ValidateBinder(CollectionManager.WishList);
  }

  [Test]
  public void Test_Decks()
  {
    var deck = CollectionManager.Decks.First();
    Log.Debug("Got deck: {Name} ({Id})", deck.Name, deck.Id);
    ValidateDeck(deck);
    ValidateDeck(CollectionManager.GetDeck(deck.Id));
  }

  [Test]
  [NonParallelizable]
  [CancelAfter(/* 2 min */ 120_000)]
  public async Task Test_DeckLifecycle()
  {
    const int CatalogId = 65378;
    const uint GreenAnnotation = 0x10;
    var requestedName = $"{TestDeckNamePrefix}{Guid.NewGuid():N}"[..21];
    var createdDecks = new List<Deck>();

    //
    // Removes any stale test decks leftover from the previous CI job in the
    // event of a failed run so that the client isn't left in a bad state in
    // which later (non-concurrent) CI jobs fail.
    //
    DeleteStaleTestDecks();

    try
    {
      //
      // Import a new deck (Add mode) and wait for MTGO to register it.
      //

      var imported = CollectionManager.ImportDeck(new Deck(
        [new CardQuantityPair(CatalogId, 1)],
        Array.Empty<CardQuantityPair>(),
        requestedName));
      createdDecks.Add(imported);

      Assert.That(
        await WaitUntilAsync(
          async () => imported.NetDeckId > 0,
          retries: 120 /* 30 seconds */),
        Is.True, "MTGO to assign the imported deck an id");
      int importedId = imported.NetDeckId;

      Assert.That(
        await WaitUntilAsync(
          async () => CollectionManager.Decks.Any(deck => deck.Id == importedId),
          retries: 120 /* 30 seconds */),
        Is.True, "the imported deck to appear in CollectionManager.Decks");

      // Verify the imported deck is visible and addressable by id and name.
      var listed = CollectionManager.Decks.Single(deck => deck.Id == importedId);
      Assert.That(listed.Name, Is.EqualTo(requestedName));
      Assert.That(CollectionManager.GetDeck(importedId).Name, Is.EqualTo(requestedName));
      Assert.That(CollectionManager.GetDeck(requestedName).Id, Is.EqualTo(importedId));
      Assert.That(listed.GetQuantity(DeckRegion.MainDeck, CatalogId), Is.EqualTo(1));

      //
      // Add cards to the main deck and confirm the quantity increased.
      //

      Assert.That(listed.AddCards(
        DeckRegion.MainDeck,
        [new CardQuantityPair(CatalogId, 2)]), Is.True);
      CollectionManager.SavePendingUpdates();

      var modified = CollectionManager.GetDeck(importedId);
      Assert.That(modified.GetQuantity(DeckRegion.MainDeck, CatalogId), Is.EqualTo(3));

      //
      // Remove cards and confirm the reported removal and new quantity.
      //

      var removed = modified.RemoveCards(
        DeckRegion.MainDeck,
        [new CardQuantityPair(CatalogId, 1)]);
      CollectionManager.SavePendingUpdates();

      Assert.That(removed, Has.Count.EqualTo(1));
      Assert.That(removed[0].Id, Is.EqualTo(CatalogId));
      Assert.That(removed[0].Quantity, Is.EqualTo(1));
      Assert.That(
        CollectionManager.GetDeck(importedId).GetQuantity(DeckRegion.MainDeck, CatalogId),
        Is.EqualTo(2));

      // Reconcile the deck to an exact desired state via ReplaceCards.
      CardGroupingItemSnapshot[] desired =
      [
        new(CatalogId, DeckRegion.MainDeck, GreenAnnotation, 2),
        new(CatalogId, DeckRegion.CommandZone, GreenAnnotation, 1),
      ];
      Assert.That(
        modified.ReplaceCards(desired, enforceLegality: false),
        Is.EquivalentTo(desired));
      CollectionManager.SavePendingUpdates();
      Assert.That(
        CollectionManager.GetDeck(importedId).GetItemSnapshot(),
        Is.EquivalentTo(desired));

      //
      // Re-import in Replace mode to update the existing deck in place.
      //

      var replaced = CollectionManager.ImportDeck(new Deck(
        [new CardQuantityPair(CatalogId, 3, GreenAnnotation)],
        [new CardQuantityPair(CatalogId, 1)],
        requestedName),
        DeckImportMode.Replace);
      CollectionManager.SavePendingUpdates();

      CardGroupingItemSnapshot[] replacement =
      [
        new(CatalogId, DeckRegion.MainDeck, GreenAnnotation, 3),
        new(CatalogId, DeckRegion.Sideboard, 0, 1),
      ];
      Assert.That(replaced.Id, Is.EqualTo(importedId));
      Assert.That(replaced.Name, Is.EqualTo(requestedName));
      Assert.That(replaced.GetItemSnapshot(), Is.EquivalentTo(replacement));

      //
      // Importing the same name again (Add mode) must collide and suffix.
      //

      string expectedCollisionName = CollectionManager.GetUniqueDeckName(requestedName);
      Assert.That(expectedCollisionName, Is.Not.EqualTo(requestedName));

      var collision = CollectionManager.ImportDeck(new Deck(
        [new CardQuantityPair(CatalogId, 1)],
        Array.Empty<CardQuantityPair>(),
        requestedName));
      createdDecks.Add(collision);
      Assert.That(
        await WaitUntilAsync(
          async () => collision.NetDeckId > 0,
          retries: 120 /* 30 seconds */),
        Is.True, "MTGO to assign the same-name deck an id");
      int collisionId = collision.NetDeckId;
      Assert.That(
        await WaitUntilAsync(
          async () => CollectionManager.Decks.Any(deck => deck.Id == collisionId),
          retries: 120 /* 30 seconds */),
        Is.True, "the same-name deck to appear in CollectionManager.Decks");

      Assert.That(collisionId, Is.Not.EqualTo(importedId));
      Assert.That(collision.Name, Is.EqualTo(expectedCollisionName));
      Assert.That(
        CollectionManager.GetDeck(collisionId).GetQuantity(DeckRegion.MainDeck, CatalogId),
        Is.EqualTo(1));

      //
      // Delete both decks and confirm they are gone.
      //

      CollectionManager.DeleteDeck(CollectionManager.GetDeck(collisionId));
      CollectionManager.DeleteDeck(CollectionManager.GetDeck(importedId));
      createdDecks.Clear();

      Assert.That(CollectionManager.Decks.Any(deck => deck.Id == collisionId), Is.False);
      Assert.That(CollectionManager.Decks.Any(deck => deck.Id == importedId), Is.False);
      Assert.Throws<KeyNotFoundException>(() => CollectionManager.GetDeck(collisionId));
      Assert.Throws<KeyNotFoundException>(() => CollectionManager.GetDeck(importedId));
    }
    finally
    {
      // Delete any decks that were created but not yet cleaned up.
      foreach (var candidate in createdDecks.AsEnumerable().Reverse())
      {
        int candidateId = candidate.NetDeckId;
        string candidateName = candidate.Name;

        var cleanupDeck = CollectionManager.Decks.SingleOrDefault(deck =>
          (candidateId <= 0 || deck.Id == candidateId) &&
          string.Equals(deck.Name, candidateName, StringComparison.Ordinal));

        if (cleanupDeck is not null)
          CollectionManager.DeleteDeck(cleanupDeck);
      }
    }
  }

  private const string TestDeckNamePrefix = "SDK Test ";

  /// <summary>
  /// Deletes any test decks left over from previous failed runs so the
  /// lifecycle test is idempotent and does not accumulate garbage in the
  /// account. Materializes the match first because deleting mutates the
  /// underlying collection being enumerated.
  /// </summary>
  private static void DeleteStaleTestDecks()
  {
    var stale = CollectionManager.Decks
      .Where(deck => deck.Name.StartsWith(TestDeckNamePrefix, StringComparison.Ordinal))
      .ToList();
    foreach (var deck in stale)
    {
      CollectionManager.DeleteDeck(deck);
    }
  }

  [Test]
  public void Test_Cards()
  {
    var card = CollectionManager.GetCard(65378);
    ValidateCard(card);
    Assert.That(card.Name, Is.EqualTo("Colossal Dreadmaw"));
    ValidateCard(CollectionManager.GetCard("Colossal Dreadmaw"));

    Assert.That(
      CollectionManager.GetCardIds("Colossal Dreadmaw"),
      Is.Not.Empty);
    Assert.That(
      CollectionManager.GetCards("Colossal Dreadmaw").Any(e => e.Id == 65378),
      Is.True);

    Assert.That(card.Colors, Is.EqualTo("G"));
    Assert.That(card.ManaCost, Is.EqualTo("{4}{G}{G}"));
    Assert.That(card.ConvertedManaCost, Is.EqualTo(6));
    Assert.That(card.RulesText,
        Is.EqualTo("Trample @i(This creature can deal excess combat damage to the player or planeswalker it's attacking.)@i"));
    Assert.That(card.Types, Is.EquivalentTo(new string[] { "Creature" }));
    Assert.That(card.Subtypes, Is.EquivalentTo(new string[] { "Dinosaur" }));
    Assert.That(card.Artist, Is.EqualTo("Jesper Ejsing"));
    Assert.That(card.ArtId, Is.EqualTo(402333));
    Assert.That(card.Set?.Code, Is.EqualTo("XLN"));
    Assert.That(card.CollectorInfo, Is.EqualTo("180/279"));
    Assert.That(card.CollectorNumber, Is.EqualTo(180));
    Assert.That(card.FlavorText,
        Is.EqualTo("@iIf you feel the ground quake, run. If you hear its bellow, flee. If you see its teeth, it's too late.@i"));
    Assert.That(card.Power, Is.EqualTo("6"));
    Assert.That(card.Toughness, Is.EqualTo("6"));
    Assert.That(card.Loyalty, Is.EqualTo("0"));
    Assert.That(card.Defense, Is.EqualTo("0"));
    Assert.That(card.IsToken, Is.False);

    Assert.That((string)card, Is.EqualTo("Colossal Dreadmaw"));
    Assert.That((int)card, Is.EqualTo(65378));
  }

  [Test]
  public void Test_Sets()
  {
    var set = CollectionManager.GetSet("XLN");
    ValidateSet(set);

    // Ensure that invalid set codes do not return an invalid set object
    Assert.Throws<KeyNotFoundException>(() => CollectionManager.GetSet("$_NA"));
  }
}

public abstract class CollectionValidationFixture : BaseFixture
{
  private void ValidateCardGrouping(CardGrouping grouping)
  {
    // If the grouping is another view of the collection, it will not have an id
    bool isCollectionView =
      grouping.GetType() == typeof(MTGOSDK.API.Collection.Collection) ||
      grouping.Name == "Full Trade List";

    // ICardGrouping properties
    Assert.That(grouping.Id,
        isCollectionView ? Is.EqualTo(0) : Is.GreaterThan(0));
    Assert.That((string?)grouping.Name,
        isCollectionView ? Is.Not.Null : Is.Not.Empty);
    Assert.That(grouping.Format?.Name, Is.Not.Empty.Or.EqualTo(null));
    Assert.That(grouping.Timestamp,
      isCollectionView
        ? Is.GreaterThanOrEqualTo(DateTime.MinValue)
        : Is.GreaterThan(DateTime.Parse("1970-01-01",
                                         CultureInfo.InvariantCulture,
                                         DateTimeStyles.AssumeUniversal)));
    Assert.That(grouping.ItemCount, Is.GreaterThanOrEqualTo(0));
    Assert.That(grouping.MaxItems,
        Is.GreaterThanOrEqualTo(isCollectionView ? -1 : 0));
    Assert.That(grouping.Hash, Is.Not.Empty);
    if (grouping.Id > 0)
    {
      Assert.That(grouping.Items?.Take(5),
          grouping.ItemCount == 0 ? Is.Empty : Is.Not.Empty);
      Assert.That(grouping.ItemIds?.Take(5),
          grouping.ItemCount == 0 ? Is.Empty : Is.Not.Empty);
    }
  }

  private void ValidateCardQuantityPair(CardQuantityPair pair)
  {
    Assert.That(pair.Id, Is.GreaterThan(0));
    // Assert.That(pair.Hash, Is.GreaterThan(0)); // Investigate @base.Key property
    Assert.That(pair.Card.Id, Is.GreaterThan(0));
    Assert.That(pair.Quantity, Is.GreaterThanOrEqualTo(0));
  }

  private void ValidateCollectionItem<T>(CollectionItem<T> item) where T : class
  {
    // IMagicEntityDefinition properties
    Assert.That(item.Id, Is.GreaterThan(0));
    Assert.That(item.Name, Is.Not.Empty);
    Assert.That(item.Description, Is.Not.Empty);
    Assert.That((bool?)item.IsOpenable, Is.Not.Null);
    Assert.That((bool?)item.IsSealedProduct, Is.Not.Null);
    Assert.That((bool?)item.IsDigitalObject, Is.Not.Null);
    Assert.That((bool?)item.IsBooster, Is.Not.Null);
    Assert.That((bool?)item.IsCard, Is.Not.Null);
    Assert.That((bool?)item.IsTicket, Is.Not.Null);
    Assert.That((bool?)item.IsTradeable, Is.Not.Null);
    Assert.That((bool?)item.HasPremiumOdds, Is.Not.Null);
  }

  public void ValidateCollection(MTGOSDK.API.Collection.Collection collection)
  {
    // ICardGrouping properties
    ValidateCardGrouping(collection);
  }

  public void ValidateBinder(Binder binder)
  {
    // ICardGrouping properties
    ValidateCardGrouping(binder);

    // IBinder properties
    Assert.That(binder.IsLastUsedBinder,
      CollectionManager.LastUsedBinder?.Id == binder.Id ? Is.True : Is.False);
    Assert.That(binder.IsWishList,
      CollectionManager.WishList?.Id == binder.Id ? Is.True : Is.False);
    Assert.That(binder.IsMegaBinder,
        binder.MaxItems == 0 ? Is.True : Is.False);
  }

  public void ValidateDeck(Deck deck)
  {
    // ICardGrouping properties
    ValidateCardGrouping(deck);

    // IDeck properties
    Assert.That(deck.Regions, Is.Not.Empty);
    Assert.That(deck.DeckId,
      deck.Name == "New Account Starter Kit Contents"
        ? Is.EqualTo(0)
        : Is.GreaterThan(0));
    Assert.That((bool?)deck.IsLegal, Is.Not.Null);

    // IDeck methods
    int maindeckCount = deck.GetRegionCount(DeckRegion.MainDeck);
    Assert.That(maindeckCount, Is.GreaterThanOrEqualTo(0));
    if (maindeckCount > 0)
    {
      IEnumerable<CardQuantityPair> maindeck = deck.GetCards(DeckRegion.MainDeck);
      Assert.That(maindeck, Is.Not.Empty);
      foreach(CardQuantityPair pair in maindeck.Take(5))
      {
        ValidateCardQuantityPair(pair);
      }
    }

    // Test special zones/cases that vendor friendly names on the client.
    int commandZoneCount = deck.GetRegionCount(DeckRegion.CommandZone);
    Assert.That(commandZoneCount, Is.GreaterThanOrEqualTo(0));
  }

  public void ValidateCard(Card card)
  {
    // IMagicEntityDefinition properties
    ValidateCollectionItem<Card>(card);

    // ICardDefinition properties
    Assert.That(card.Colors, Is.Not.Empty);
    Assert.That(card.ManaCost, Is.Not.Empty);
    Assert.That(card.ConvertedManaCost, Is.GreaterThanOrEqualTo(0));

    Assert.That((string?)card.RulesText, Is.Not.Null);
    Assert.That(card.Types.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(card.Subtypes.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(card.Artist, Is.Not.Empty);
    Assert.That(card.ArtId, Is.GreaterThan(0));
    Assert.That(card.Set?.Code, Is.Not.Null.And.Not.Empty);
    Assert.That(card.CollectorInfo, Is.Not.Empty);
    Assert.That(card.CollectorNumber, Is.GreaterThanOrEqualTo(0));
    Assert.That((string?)card.FlavorText, Is.Not.Null);
    Assert.That((string?)card.Power, Is.Not.Null);
    Assert.That((string?)card.Toughness, Is.Not.Null);
    Assert.That((string?)card.Loyalty, Is.Not.Null);
    Assert.That((string?)card.Defense, Is.Not.Null);

    Assert.That((int?)card, Is.EqualTo(card.Id));
    Assert.That((string?)card, Is.EqualTo(card.Name));
  }

  public void ValidateSet(Set set)
  {
    // ICardSet properties
    Assert.That(set.Code, Is.Not.Empty);
    Assert.That(set.Name, Is.Not.Empty);
    Assert.That(set.ReleaseDate, Is.GreaterThan(DateTime.MinValue));
    Assert.That(set.Type, Is.Not.EqualTo(SetType.NotSet));
    Assert.That(set.Age, Is.GreaterThanOrEqualTo(1));
    Assert.That(set.Cards.Take(5), Is.Not.Empty);

    // ICardSet methods
    var card = set.Cards.First();
    Assert.That(set.ContainsCatalogId(card.Id), Is.True);
    Assert.That(set.ContainsCard(card), Is.True);
    Assert.That((string)set, Is.EqualTo($"{set.Name} ({set.Code})"));
  }
}
