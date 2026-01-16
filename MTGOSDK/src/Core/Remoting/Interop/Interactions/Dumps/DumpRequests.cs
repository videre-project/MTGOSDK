/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

[MessagePackObject]
public class HeapDumpRequest
{
  [Key(0)]
  public string TypeFilter { get; set; }
  [Key(1)]
  public bool DumpHashcodes { get; set; }
}

[MessagePackObject]
public class TypesDumpRequest
{
  [Key(0)]
  public string Assembly { get; set; }
}
