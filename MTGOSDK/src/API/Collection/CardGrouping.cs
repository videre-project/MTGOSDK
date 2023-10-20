/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Collection;
using WotC.MtGO.Client.Model.Core.Collection;


namespace MTGOSDK.API.Collection;

public abstract class CardGrouping<T> : DLRWrapper<ICardGrouping>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  internal override Type type => typeof(T);

  //
  // ICardGrouping derived properties
  //

  /// <summary>
  /// The unique identifier for this item.
  /// </summary>
  public int Id => @base.NetDeckId;

  /// <summary>
  /// The user-defined name for this item.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The format this item is associated with. (e.g. Standard, Historic, etc.)
  /// </summary>
  public IPlayFormat Format => Proxy<IPlayFormat>.As(@base.Format);

  /// <summary>
  /// The timestamp of the last modification to this item.
  /// </summary>
  public DateTime Timestamp => @base.Timestamp;

  /// <summary>
  /// The total number of cards contained in this item.
  /// </summary>
  public int ItemCount => @base.ItemCount;

  /// <summary>
  /// The maximum number of cards that can be contained in this item.
  /// </summary>
  public int MaxItems => @base.MaxItems;

  /// <summary>
  /// The hash of the contents of this item.
  /// </summary>
  public string Hash => @base.CurrentHash;

  // public IEnumerable<ICardQuantityPair> Items => @base.Items;
  // public IEnumerable<int> ItemIds => @base.ItemIds;
}
