/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  public object _registeredPidsLock = new();
  public List<int> _registeredPids = new();

  private byte[] MakeRegisterClientResponse()
  {
    var request = DeserializeRequest<RegisterClientRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int pid = request.ProcessId;

    lock (_registeredPidsLock)
    {
      _registeredPids.Add(pid);
      _clientCallbacks.TryAdd(pid, new HashSet<int>());
    }
    Log.Debug("[Diver] New client registered. ID = " + pid);
    return s_okResponse;
  }

  private byte[] MakeUnregisterClientResponse()
  {
    var request = DeserializeRequest<UnregisterClientRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int pid = request.ProcessId;

    bool removed;
    int remaining;
    lock (_registeredPidsLock)
    {
      removed = _registeredPids.Remove(pid);
      remaining = _registeredPids.Count;
      if (remaining == 0) _runtime?.UnpinAllObjects();

      if (_clientCallbacks.TryRemove(pid, out var tokens))
      {
        foreach (var token in tokens)
        {
          if (_callbackTokens.TryRemove(token, out var cts))
          {
            cts.Cancel();
            cts.Dispose();
          }

          if (_remoteEventHandler.TryRemove(token, out var eventInfo))
            eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);

          if (_remoteHooks.TryRemove(token, out var hookInfo))
            HarmonyWrapper.Instance.RemovePrefix(hookInfo.OriginalHookedMethod);
        }
      }
    }
    Log.Debug("[Diver] Client unregistered. ID = " + pid);

    var ucResponse = new UnregisterClientResponse
    {
      WasRemoved = removed,
      OtherClientsAmount = remaining
    };

    return WrapSuccess(ucResponse);
  }
}
