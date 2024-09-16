/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MTGOSDK.Core.Logging;


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
