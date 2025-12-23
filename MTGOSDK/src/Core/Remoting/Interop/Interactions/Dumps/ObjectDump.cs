/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class ObjectDump
{
  [Key(0)]
  public ObjectType ObjectType { get; set; }
  [Key(1)]
  public ObjectType SubObjectsType { get; set; }

  /// <summary>
  /// Address where the item was retrieved from
  /// </summary>
  [Key(2)]
  public ulong RetrievalAddress { get; set; }
  /// <summary>
  /// Address when the item was freezed at when pinning. This address won't change until unpinning.
  /// </summary>
  [Key(3)]
  public ulong PinnedAddress { get; set; }
  [Key(4)]
  public string Type { get; set; }
  [Key(5)]
  public string PrimitiveValue { get; set; }
  /// <summary>
  /// Number of elemnets in the array. This field is only meaningful if ObjectType is "Array"
  /// </summary>
  [Key(6)]
  public int SubObjectsCount { get; set; }
  [Key(7)]
  public List<MemberDump> Fields { get; set; }
  [Key(8)]
  public List<MemberDump> Properties { get; set; }
  [Key(9)]
  public int HashCode { get; set; }
}
