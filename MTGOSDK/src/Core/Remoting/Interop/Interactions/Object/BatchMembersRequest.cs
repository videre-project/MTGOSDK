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
/// Response containing batch-fetched property values.
/// </summary>
[MessagePackObject]
public class BatchMembersResponse
{
  /// <summary>
  /// Dictionary of path to serialized value.
  /// Values are encoded as strings using PrimitivesEncoder where possible.
  /// Non-primitive values return their remote address as string.
  /// </summary>
  [Key(0)]
  public Dictionary<string, string> Values { get; set; }

  /// <summary>
  /// Dictionary of path to type full name.
  /// </summary>
  [Key(1)]
  public Dictionary<string, string> Types { get; set; }
}
