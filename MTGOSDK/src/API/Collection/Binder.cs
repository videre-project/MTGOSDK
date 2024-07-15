/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Collection;


namespace MTGOSDK.API.Collection;
using static MTGOSDK.API.Events;

public sealed class Binder(dynamic binder) : CardGrouping<Binder>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IBinder);

  /// <summary>
  /// Stores an internal reference to the IBinder object.
  /// </summary>
  internal override dynamic obj => binder;

  //
  // IBinder wrapper properties
  //

  public bool IsLastUsedBinder => @base.IsLastUsedBinder;

  public bool IsWishList => @base.IsWishList;

  public bool IsMegaBinder => @base.IsMegaBinder;

  //
  // ICardGrouping wrapper events
  //

  public EventProxy<CardGroupingItemsChangedEventArgs> ItemsAddedOrRemoved =
    new(/* ICardGrouping */ binder, nameof(ItemsAddedOrRemoved));
}
