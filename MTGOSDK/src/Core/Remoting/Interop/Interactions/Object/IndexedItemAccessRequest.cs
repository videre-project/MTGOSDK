/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

[MessagePackObject]
public class IndexedItemAccessRequest
{
  [Key(0)]
  public ulong CollectionAddress { get; set; }
  [Key(1)]
  public bool PinRequest { get; set; }
  [Key(2)]
  public ObjectOrRemoteAddress Index { get; set; }
  [Key(3)]
  public bool ForceUIThread { get; set; }
}

