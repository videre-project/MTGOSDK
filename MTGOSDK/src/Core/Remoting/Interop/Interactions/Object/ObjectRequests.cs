/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

[MessagePackObject]
public class UnpinRequest
{
  [Key(0)]
  public ulong Address { get; set; }
}

[MessagePackObject]
public class ObjectDumpRequest
{
  [Key(0)]
  public ulong Address { get; set; }
  [Key(1)]
  public string TypeName { get; set; }
  [Key(2)]
  public bool PinRequest { get; set; }
  [Key(3)]
  public int? Hashcode { get; set; }
  [Key(4)]
  public bool HashcodeFallback { get; set; }
}
