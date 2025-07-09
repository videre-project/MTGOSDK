/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core;

namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private int _nextAvailableCallbackToken;

  public int AssignCallbackToken() =>
    Interlocked.Increment(ref _nextAvailableCallbackToken);

  private readonly ConcurrentDictionary<IPEndPoint, ReverseCommunicator> _reverseCommunicators = new();

  private bool IsPortOpen(int port)
  {
    try
    {
      using (var tcpClient = new TcpClient())
      {
        // Try to connect to the port
        tcpClient.Connect(IPAddress.Loopback, port);
        return true; // Port is open
      }
    }
    catch (SocketException)
    {
      // If we can't connect, the port is likely closed
      return false;
    }
  }

  public async Task InvokeControllerCallback(
    IPEndPoint callbacksEndpoint,
    int token,
    DateTime timestamp,
    params object[] parameters)
  {
    if (!_callbackTokens.TryGetValue(token, out var cts))
      return;

    ulong? addr = null;
    var remoteParams = new ObjectOrRemoteAddress[parameters.Length];
    for (int i = 0; i < parameters.Length; i++)
    {
      object parameter = parameters[i];
      if (parameter == null)
      {
        remoteParams[i] = ObjectOrRemoteAddress.Null;
      }
      else if (parameter.GetType().IsPrimitiveEtc())
      {
        remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
      }
      else
      {
        addr = _runtime.PinObject(parameter);
        remoteParams[i] = ObjectOrRemoteAddress.FromToken(
          addr.Value,
          parameter.GetType().FullName);
      }
    }

    // Call callback at controller
    ReverseCommunicator reverseCommunicator = _reverseCommunicators
      .GetOrAdd(callbacksEndpoint,
        endpoint => new ReverseCommunicator(endpoint, cts));
    try
    {
      await reverseCommunicator.InvokeCallback(token, timestamp, remoteParams);
    }
    catch (Exception)
    {
      // If the port is closed or the controller is dead, remove the callback.
      if (!IsPortOpen(callbacksEndpoint.Port) ||
          !await reverseCommunicator.CheckIfAlive())
      {
        // Cancel any pending requests
        reverseCommunicator.Cancel();

        // Remove the callback token from the dictionary
        _remoteEventHandler.TryRemove(token, out _);
        _remoteHooks.TryRemove(token, out _);
        _reverseCommunicators.TryRemove(callbacksEndpoint, out _);

        // Unpin the object if it was pinned
        if (addr != null) _runtime.UnpinObject(addr.Value);
      }
    }
  }

  private string MakeEventSubscribeResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    string ipAddrStr = arg.QueryString.Get("ip");
    string portStr = arg.QueryString.Get("port");
    if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
    {
      return QuickError("Missing parameter 'address' (object address)");
    }
    if (!(IPAddress.TryParse(ipAddrStr, out IPAddress ipAddress) && int.TryParse(portStr, out int port)))
    {
      return QuickError("Failed to parse either IP Address ('ip' param) or port ('port' param)");
    }
    IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
    Log.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

    // Check if we have this objects in our pinned pool
    if (!_runtime.TryGetPinnedObject(objAddr, out object target))
    {
      // Object not pinned, try get it the hard way
      return QuickError("Object at given address wasn't pinned (context: RegisterEventHandler)");
    }

    Type resolvedType = target.GetType();

    string eventName = arg.QueryString.Get("event");
    if (eventName == null)
    {
      return QuickError("Missing parameter 'event'");
    }
    // TODO: Does this need to be done recursivly?
    EventInfo eventObj = resolvedType.GetEvent(eventName);
    if (eventObj == null)
    {
      return QuickError("Failed to find event in type");
    }

    // Let's make sure the event's delegate type has 2 args - (object, EventArgs or subclass)
    Type eventDelegateType = eventObj.EventHandlerType;
    MethodInfo invokeInfo = eventDelegateType.GetMethod("Invoke");
    ParameterInfo[] paramInfos = invokeInfo.GetParameters();
    if (paramInfos.Length != 2)
    {
      return QuickError("Currently only events with 2 parameters (object & EventArgs) can be subscribed to.");
    }

    int token = AssignCallbackToken();
    _callbackTokens[token] = new CancellationTokenSource();

    // Associate the token with the client port
    int clientPort = arg.RemoteEndPoint.Port;
    lock (_registeredPidsLock)
    {
      if (_clientCallbacks.TryGetValue(clientPort, out var tokens))
      {
        tokens.Add(token);
      }
    }

    string groupId = $"Event:{resolvedType.FullName}.{eventName}";
    EventHandler eventHandler = (obj, args) =>
    {
      // Get current timestamp
      DateTime timestamp = DateTime.Now;
      _ = SyncThread.EnqueueAsync(
          async () => await InvokeControllerCallback(endpoint, token, timestamp, [obj, args]),
          groupId,
          TimeSpan.FromSeconds(5));
    };

    try
    {
      Type eventArgsType = paramInfos[1].ParameterType;
      var wrapperType = typeof(EventWrapper<>).MakeGenericType(eventArgsType);

      // Pass the target object and event name to the wrapper
      var wrapperInstance = Activator.CreateInstance(
        wrapperType,
        eventHandler);

      Delegate my_delegate = Delegate.CreateDelegate(eventDelegateType, wrapperInstance, "Handle");

      Log.Debug($"[Diver] Adding event handler to event {eventName}...");
      eventObj.AddEventHandler(target, my_delegate);
      Log.Debug($"[Diver] Added event handler to event {eventName}!");

      // Save all the registeration info so it can be removed later upon request
      _remoteEventHandler[token] = new RegisteredEventHandlerInfo()
      {
        EventInfo = eventObj,
        Target = target,
        RegisteredProxy = my_delegate,
        Endpoint = endpoint
      };
    }
    catch (Exception ex)
    {
      return QuickError($"Failed insert the event handler: {ex.ToString()}");
    }

    EventRegistrationResults erResults = new() { Token = token };
    return JsonConvert.SerializeObject(erResults);
  }
}
