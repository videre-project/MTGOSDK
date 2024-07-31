/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Reflection;

using CollectionItem = MTGOSDK.API.Collection.CollectionItem<dynamic>;


namespace MTGOSDK.API.Collection;

public class ItemCollection(dynamic itemCollection) : DLRWrapper<dynamic>
{
  /// <summary>
  /// Stores an internal reference to the ItemCollection object.
  /// </summary>
  internal override dynamic obj => itemCollection; // Input obj is not type-casted.

  //
  // ItemCollection wrapper properties
  //

  public int Count => @base.Count;

	public List<CollectionItem> CollectionItems =>
    Map<IList, CollectionItem>(@base.CollectionItems);

	public List<CollectionItem> OpenableItems =>
    Map<IList, CollectionItem>(@base.OpenableItems);

  //
  // ItemCollection wrapper methods
  //

  // TODO:
  //   - AddItem, RemoveItem
  //   - AddRange, RemoveRange

  public List<CollectionItem> GetItem(int id) =>
    Map<IList, CollectionItem>(@base.GetItemById(id));

  public bool Contains(int id) => @base.Contains(id);

  /// <summary>
  /// Determines if the collection contains only a single item with the given ID.
  /// </summary>
  public bool StrictlyContains(int id) => @base.StrictlyContains(id);

  public override string ToString() => @base.ToString();
}
