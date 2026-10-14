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

  /// <summary>
  /// Whether to skip pinning each item and returning its token in
  /// <see cref="BatchCollectionResponse.ItemTokens"/>. Callers that do not wrap
  /// the tokens in remote objects (which unpin them when collected) set this:
  /// an unwrapped pin is never released. False when absent, so an older
  /// client still receives its item tokens.
  /// </summary>
  [Key(4)]
  public bool SkipItemPins { get; set; }
}

/// <summary>
/// Response containing batch-fetched property values for all collection items
/// in a columnar layout. Property names and types appear once in the schema;
/// values are stored as <c>Columns[propertyIndex][itemIndex]</c>.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
public class BatchCollectionResponse : IPinResponse
{
  [IgnoreMember]
  internal string? PinOwner { get; set; }

  [IgnoreMember]
  string? IPinResponse.PinOwner
  {
    set => PinOwner = value;
  }

  /// <summary>
  /// Property path names (e.g., "Name", "Id"). Defines column order.
  /// </summary>
  [Key(0)]
  public string[] Schema { get; set; }

  /// <summary>
  /// Type full names, parallel to <see cref="Schema"/>.
  /// </summary>
  [Key(1)]
  public string[] SchemaTypes { get; set; }

  /// <summary>
  /// Columnar values: <c>Columns[propertyIndex][itemIndex]</c>.
  /// Each inner array has <see cref="Count"/> elements.
  /// Values are PrimitivesEncoder-encoded strings; null for unresolvable values.
  /// </summary>
  [Key(2)]
  public string?[][] Columns { get; set; }

  /// <summary>
  /// Total number of items processed.
  /// </summary>
  [Key(3)]
  public int Count { get; set; }

  /// <summary>
  /// Remote tokens (addresses) for each pinned item, in item order, when the
  /// request did not set <see cref="BatchCollectionRequest.SkipItemPins"/>;
  /// otherwise empty.
  /// Used to create DRO references for DLRWrapper fallback access.
  /// </summary>
  [Key(4)]
  public List<ulong> ItemTokens { get; set; }

  /// <summary>
  /// Full type name for items in the collection.
  /// </summary>
  [Key(5)]
  public string ItemTypeName { get; set; }
}
