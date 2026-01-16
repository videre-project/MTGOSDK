/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

/// <summary>
/// Request for batch fetching property paths from all items in a collection.
/// </summary>
[MessagePackObject]
public class BatchCollectionRequest
{
  /// <summary>
  /// Address of the collection object (IEnumerable).
  /// </summary>
  [Key(0)]
  public ulong CollectionAddress { get; set; }

  /// <summary>
  /// Full type name of the collection.
  /// </summary>
  [Key(1)]
  public string CollectionTypeName { get; set; }

  /// <summary>
  /// Pipe-delimited property paths to fetch for each item (e.g., "Name|Id|Rarity.Name").
  /// </summary>
  [Key(2)]
  public string PathsDelimited { get; set; }

  /// <summary>
  /// Maximum number of items to process (0 = no limit).
  /// </summary>
  [Key(3)]
  public int MaxItems { get; set; }
}

/// <summary>
/// Response containing batch-fetched property values for all collection items.
/// </summary>
[MessagePackObject]
public class BatchCollectionResponse
{
  /// <summary>
  /// List of items, each containing a dictionary of path to serialized value.
  /// </summary>
  [Key(0)]
  public List<Dictionary<string, string>> Items { get; set; }

  /// <summary>
  /// Dictionary of path to type full name (shared across all items).
  /// </summary>
  [Key(1)]
  public Dictionary<string, string> Types { get; set; }

  /// <summary>
  /// Total number of items processed.
  /// </summary>
  [Key(2)]
  public int Count { get; set; }

  /// <summary>
  /// Remote tokens (addresses) for each pinned item, in same order as Items.
  /// Used to create DRO references for DLRWrapper fallback access.
  /// </summary>
  [Key(3)]
  public List<ulong> ItemTokens { get; set; }

  /// <summary>
  /// Full type name for items in the collection.
  /// </summary>
  [Key(4)]
  public string ItemTypeName { get; set; }
}
