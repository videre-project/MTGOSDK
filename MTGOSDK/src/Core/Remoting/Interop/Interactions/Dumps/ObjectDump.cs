﻿/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

public class ObjectDump
{
  public ObjectType ObjectType { get; set; }
  public ObjectType SubObjectsType { get; set; }

  /// <summary>
  /// Address where the item was retrieved from
  /// </summary>
  public ulong RetrievalAddress { get; set; }
  /// <summary>
  /// Address when the item was freezed at when pinning. This address won't change until unpinning.
  /// </summary>
  public ulong PinnedAddress { get; set; }
  public string Type { get; set; }
  public string PrimitiveValue { get; set; }
  /// <summary>
  /// Number of elemnets in the array. This field is only meaningful if ObjectType is "Array"
  /// </summary>
  public int SubObjectsCount { get; set; }
  public List<MemberDump> Fields { get; set; }
  public List<MemberDump> Properties { get; set; }
  public int HashCode { get; set; }
}
