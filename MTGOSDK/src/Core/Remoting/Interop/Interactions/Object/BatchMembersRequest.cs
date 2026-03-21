/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

/// <summary>
/// Request for batch fetching multiple property paths from an object.
/// </summary>
[MessagePackObject]
public class BatchMembersRequest
{
  /// <summary>
  /// Address of the target remote object.
  /// </summary>
  [Key(0)]
  public ulong ObjAddress { get; set; }

  /// <summary>
  /// Full type name of the target object.
  /// </summary>
  [Key(1)]
  public string TypeFullName { get; set; }

  /// <summary>
  /// Pipe-delimited property paths (e.g., "Name|Id|Rarity.Name").
  /// </summary>
  [Key(2)]
  public string PathsDelimited { get; set; }
}

/// <summary>
/// Response containing batch-fetched property values in parallel arrays.
/// </summary>
[MessagePackObject]
public class BatchMembersResponse
{
  /// <summary>
  /// Property path names (e.g., "Name", "Id", "Rarity.Name").
  /// </summary>
  [Key(0)]
  public string[] Schema { get; set; }

  /// <summary>
  /// Type full names, parallel to <see cref="Schema"/>.
  /// </summary>
  [Key(1)]
  public string[] SchemaTypes { get; set; }

  /// <summary>
  /// Encoded values, parallel to <see cref="Schema"/>.
  /// Values are encoded as strings using PrimitivesEncoder where possible.
  /// Non-primitive values return their remote address as string prefixed with '@'.
  /// Null entries indicate null or unresolvable values.
  /// </summary>
  [Key(2)]
  public string?[] Values { get; set; }
}
