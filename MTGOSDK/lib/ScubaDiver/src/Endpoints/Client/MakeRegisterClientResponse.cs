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


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private readonly object _registeredPidsLock = new();
  private readonly List<int> _registeredPids = new();

  private byte[] MakeRegisterClientResponse()
  {
    var request = DeserializeRequest<RegisterClientRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int pid = request.ProcessId;

    lock (_registeredPidsLock)
    {
      _registeredPids.Add(pid);
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
