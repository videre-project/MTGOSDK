/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;

using Newtonsoft.Json;

using ScubaDiver.Hooking;
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
      _clientCallbacks.TryAdd(pid, new HashSet<int>());
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
      // Clean up all pinned objects if not used by any client
      if (remaining == 0) _runtime?.UnpinAllObjects();

      // Clean up all callbacks associated with this client
      if (_clientCallbacks.TryRemove(pid, out var tokens))
      {
        foreach (var token in tokens)
        {
          // Cancel token and dispose
          if (_callbackTokens.TryRemove(token, out var cts))
          {
            cts.Cancel();
            cts.Dispose();
          }

          // Remove event handlers
          if (_remoteEventHandler.TryRemove(token, out var eventInfo))
            eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);

          // Remove method hooks
          if (_remoteHooks.TryRemove(token, out var hookInfo))
            HarmonyWrapper.Instance.RemovePrefix(hookInfo.OriginalHookedMethod);
        }
      }
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
