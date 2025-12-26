/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeHeapResponse(HttpListenerRequest arg)
  {
    string filter = arg.QueryString.Get("type_filter");
    string dumpHashcodesStr = arg.QueryString.Get("dump_hashcodes");
    bool dumpHashcodes = dumpHashcodesStr?.ToLowerInvariant() == "true";

    Predicate<string> matchesFilter = typeName => typeName == filter;

    List<HeapDump.HeapObject> objects = _runtime.GetHeapObjects(
      matchesFilter,
      dumpHashcodes
    );

    var hd = new HeapDump { Objects = objects };
    return WrapSuccess(hd);
  }
}
