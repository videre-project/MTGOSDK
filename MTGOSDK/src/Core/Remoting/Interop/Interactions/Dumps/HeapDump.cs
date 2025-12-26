/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class HeapDump
{
  [MessagePackObject]
  public struct HeapObject
  {
    [Key(0)]
    public ulong Address { get; set; }
    [Key(1)]
    public string Type { get; set; }
    [Key(2)]
    public int HashCode { get; set; }
    [Key(3)]
    public ulong MethodTable { get; set; }
  }

  [Key(0)]
  public List<HeapObject> Objects { get; set; }
}
