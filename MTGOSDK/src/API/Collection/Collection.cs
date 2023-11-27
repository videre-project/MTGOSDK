/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.API.Events;

public sealed class Collection(ICollectionGrouping collection)
    : CardGrouping<ICollectionGrouping>
{
  /// <summary>
  /// Stores an internal reference to the ICollectionGrouping object.
  /// </summary>
  internal override dynamic obj => collection;

  public Collection() : this(CollectionManager.GetCollection()) { }

  //
  // ICardGrouping wrapper events
  //

  public EventProxy<CardGroupingItemsChangedEventArgs> ItemsAddedOrRemoved =
    new(/* ICardGrouping */ collection);
}
