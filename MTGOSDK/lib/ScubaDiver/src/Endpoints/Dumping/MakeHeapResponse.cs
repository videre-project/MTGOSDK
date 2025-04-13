/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;

using Newtonsoft.Json;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeHeapResponse(HttpListenerRequest arg)
  {
    string filter = arg.QueryString.Get("type_filter");
    string dumpHashcodesStr = arg.QueryString.Get("dump_hashcodes");
    bool dumpHashcodes = dumpHashcodesStr?.ToLower() == "true";

    // Default filter - Find all exact matches based on the filter type.
    Predicate<string> matchesFilter = (string typeName) => typeName == filter;

    List<HeapDump.HeapObject> objects = _runtime.GetHeapObjects(
      matchesFilter,
      dumpHashcodes
    );
    HeapDump hd = new() { Objects = objects };

    var resJson = JsonConvert.SerializeObject(hd);
    return resJson;
  }
}
