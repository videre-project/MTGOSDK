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
  private string MakeDieResponse(HttpListenerRequest req)
  {
    Log.Debug("[Diver] Die command received");
    bool forceKill = req.QueryString.Get("force")?.ToUpper() == "TRUE";
    lock (_registeredPidsLock)
    {
      if (_registeredPids.Count > 0 && !forceKill)
      {
        Log.Debug("[Diver] Die command failed - More clients exist.");
        return "{\"status\":\"Error more clients remaining. You can use the force=true argument to ignore this check.\"}";
      }
    }

    Log.Debug("[Diver] Die command accepted.");
    // _stayAlive.Reset();
    return "{\"status\":\"Goodbye\"}";
  }
}
