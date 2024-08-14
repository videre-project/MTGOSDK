/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Compiler.Snapshot;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


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

    (bool anyErrors, List<HeapDump.HeapObject> objects) = _runtime.GetHeapObjects(
      matchesFilter,
      dumpHashcodes
    );
    if (anyErrors)
    {
      return "{\"error\":\"All dumping trials failed because at least 1 " +
           "object moved between the snapshot and the heap enumeration\"}";
    }

    HeapDump hd = new() { Objects = objects };

    var resJson = JsonConvert.SerializeObject(hd);
    return resJson;
  }
}
