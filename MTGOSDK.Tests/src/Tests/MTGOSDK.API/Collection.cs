/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Logging;


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
    ValidateBinder(CollectionManager.LastUsedBinder!);
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
    Assert.That(card.ManaCost, Is.EqualTo("4GG"));
    Assert.That(card.ConvertedManaCost, Is.EqualTo(6));
    Assert.That(card.RulesText,
        Is.EqualTo("Trample @i(This creature can deal excess combat damage to the player or planeswalker it's attacking.)@i"));
    Assert.That(card.Types, Is.EquivalentTo(new string[] { "Creature" }));
    Assert.That(card.Subtypes, Is.EquivalentTo(new string[] { "Dinosaur" }));
    Assert.That(card.Artist, Is.EqualTo("Jesper Ejsing"));
    Assert.That(card.ArtId, Is.EqualTo(402333));
    Assert.That(card.Set.Code, Is.EqualTo("XLN"));
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
  private void ValidateCardGrouping<T>(CardGrouping<T> grouping) where T : class
  {
    // If the grouping is another view of the collection, it will not have an id
    bool isCollectionView =
      typeof(T) == typeof(MTGOSDK.API.Collection.Collection) ||
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
    ValidateCardGrouping<MTGOSDK.API.Collection.Collection>(collection);
  }

  public void ValidateBinder(Binder binder)
  {
    // ICardGrouping properties
    ValidateCardGrouping<Binder>(binder);

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
    ValidateCardGrouping<Deck>(deck);

    // IDeck properties
    Assert.That(deck.Regions, Is.Not.Empty);
    // Assert.That(deck.DeckId,
    //   deck.Name == "New Account Starter Kit Contents"
    //     ? Is.EqualTo(0)
    //     : Is.GreaterThan(0));
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
    Assert.That(card.Set.Code, Is.Not.Empty);
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
