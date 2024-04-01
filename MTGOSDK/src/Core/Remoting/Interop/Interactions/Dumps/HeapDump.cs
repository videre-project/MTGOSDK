/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Collections.Generic;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

public class HeapDump
{
  public struct HeapObject
  {
    public ulong Address { get; set; }
    public string Type { get; set; }
    public int HashCode { get; set; }
    public ulong MethodTable { get; set; }
  }

  public List<HeapObject> Objects { get; set; }
}
