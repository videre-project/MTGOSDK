/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Serialization;

namespace MTGOSDK.API.Collection;

public class ItemCollection(dynamic itemCollection) : DLRWrapper<dynamic>
{
  /// <summary>
  /// Stores an internal reference to the ItemCollection object.
  /// </summary>
  internal override dynamic obj => itemCollection; // Input obj is not type-casted.

  private static readonly string[] s_collectionItemBatchPaths =
  [
    "Id",
    "Quantity"
  ];

  private static readonly string[] s_cardQuantityPairBatchPaths =
  [
    "CatalogId",
    "Quantity"
  ];

  internal static List<CardQuantityPair> SerializeCollectionItems(
    dynamic collectionItems) =>
      SerializeCollectionItems(collectionItems, s_collectionItemBatchPaths);

  internal static List<CardQuantityPair> SerializeCardQuantityPairs(
    dynamic cardQuantityPairs) =>
      SerializeCollectionItems(cardQuantityPairs, s_cardQuantityPairBatchPaths);

  private static List<CardQuantityPair> SerializeCollectionItems(
      dynamic collectionItems,
      string[] batchPaths)
    {
      if (!RemoteBatchCollection.TryFetch(
            collectionItems,
            batchPaths,
            out BatchCollectionSnapshot batch))
      {
        return [];
      }

      var items = new List<CardQuantityPair>(batch.Count);
      for (int i = 0; i < batch.Count; i++)
      {
        items.Add(new CardQuantityPair(
          batch.GetInt(i, batchPaths[0]),
          batch.GetInt(i, batchPaths[1])));
      }

      return items;
    }

  //
  // ItemCollection wrapper properties
  //

  public int Count => @base.Count;

	public List<CardQuantityPair> CollectionItems =>
    SerializeCollectionItems(@base.CollectionItems);

	public List<CardQuantityPair> OpenableItems =>
    SerializeCollectionItems(@base.OpenableItems);

  //
  // ItemCollection wrapper methods
  //

  public List<CardQuantityPair> GetItem(int id) =>
    SerializeCollectionItems(@base.GetItemById(id));

  public bool Contains(int id) => @base.Contains(id);

  /// <summary>
  /// Determines if the collection contains only a single item with the given ID.
  /// </summary>
  public bool StrictlyContains(int id) => @base.StrictlyContains(id);

  public override string ToString() => @base.ToString();
}
