/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeHeapResponse()
  {
    var request = DeserializeRequest<HeapDumpRequest>();
    string filter = request?.TypeFilter;
    bool dumpHashcodes = request?.DumpHashcodes ?? false;

    Predicate<string> matchesFilter = typeName => typeName == filter;

    List<HeapDump.HeapObject> objects = _runtime.GetHeapObjects(
      matchesFilter,
      dumpHashcodes
    );

    var hd = new HeapDump { Objects = objects };
    return WrapSuccess(hd);
  }
}
