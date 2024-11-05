/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  public object _registeredPidsLock = new();
  public List<int> _registeredPids = new();

  private string MakeRegisterClientResponse(HttpListenerRequest arg)
  {
    string pidString = arg.QueryString.Get("process_id");
    if (pidString == null || !int.TryParse(pidString, out int pid))
    {
      return QuickError("Missing parameter 'process_id'");
    }
    lock (_registeredPidsLock)
    {
      _registeredPids.Add(pid);
    }
    Log.Debug("[Diver] New client registered. ID = " + pid);
    return "{\"status\":\"OK'\"}";
  }

  private string MakeUnregisterClientResponse(HttpListenerRequest arg)
  {
    string pidString = arg.QueryString.Get("process_id");
    if (pidString == null || !int.TryParse(pidString, out int pid))
    {
      return QuickError("Missing parameter 'process_id'");
    }
    bool removed;
    int remaining;
    lock (_registeredPidsLock)
    {
      removed = _registeredPids.Remove(pid);
      remaining = _registeredPids.Count;
    }
    Log.Debug("[Diver] Client unregistered. ID = " + pid);

    UnregisterClientResponse ucResponse = new()
    {
      WasRemoved = removed,
      OtherClientsAmount = remaining
    };

    return JsonConvert.SerializeObject(ucResponse);
  }
}
