/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.API.Events;

public sealed class Collection(ICollectionGrouping collection) : CardGrouping
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ICollectionGrouping);

  /// <summary>
  /// Stores an internal reference to the ICollectionGrouping object.
  /// </summary>
  internal override dynamic obj => collection;

  public Collection() : this(CollectionManager.GetCollection()) { }

  public override IReadOnlyList<CardGroupingItemSnapshot> GetItemSnapshot() =>
    GetFrozenCollection
      .Where(item => item.Quantity > 0)
      .Select(item => new CardGroupingItemSnapshot(
        item.Id,
        DeckRegion.NotSet,
        0,
        item.Quantity))
      .ToArray();

  //
  // ICardGrouping wrapper events
  //

  public EventProxy<CardGroupingItemsChangedEventArgs> ItemsAddedOrRemoved =
    new(/* ICardGrouping */ collection, nameof(ItemsAddedOrRemoved));
}
