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

using MTGOSDK;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


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
      using var tcpClient = new TcpClient();
      tcpClient.Connect(IPAddress.Loopback, port);
      return true;
    }
    catch (SocketException)
    {
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
        int hashCode = parameter.GetHashCode();
        remoteParams[i] = ObjectOrRemoteAddress.FromToken(
          addr.Value,
          parameter.GetType().FullName,
          hashCode);
      }
    }

    ReverseCommunicator reverseCommunicator = _reverseCommunicators
      .GetOrAdd(callbacksEndpoint, endpoint => new ReverseCommunicator(endpoint, cts));
    try
    {
      reverseCommunicator.InvokeCallback(token, timestamp, remoteParams);
    }
    catch (Exception)
    {
      if (!IsPortOpen(callbacksEndpoint.Port) ||
          !await reverseCommunicator.CheckIfAlive())
      {
        reverseCommunicator.Cancel();
        _remoteEventHandler.TryRemove(token, out _);
        _remoteHooks.TryRemove(token, out _);
        _reverseCommunicators.TryRemove(callbacksEndpoint, out _);
        if (addr != null) _runtime.UnpinObject(addr.Value);
      }
    }
  }

  private byte[] MakeEventSubscribeResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    string ipAddrStr = arg.QueryString.Get("ip");
    string portStr = arg.QueryString.Get("port");
    if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
      return QuickError("Missing parameter 'address' (object address)");

    if (!(IPAddress.TryParse(ipAddrStr, out IPAddress ipAddress) && int.TryParse(portStr, out int port)))
      return QuickError("Failed to parse either IP Address ('ip' param) or port ('port' param)");

    IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
    Log.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

    if (!_runtime.TryGetPinnedObject(objAddr, out object target))
      return QuickError("Object at given address wasn't pinned (context: RegisterEventHandler)");

    Type resolvedType = target.GetType();

    string eventName = arg.QueryString.Get("event");
    if (eventName == null)
      return QuickError("Missing parameter 'event'");

    EventInfo eventObj = resolvedType.GetEvent(eventName);
    if (eventObj == null)
      return QuickError("Failed to find event in type");

    Type eventDelegateType = eventObj.EventHandlerType;
    MethodInfo invokeInfo = eventDelegateType.GetMethod("Invoke");
    ParameterInfo[] paramInfos = invokeInfo.GetParameters();
    if (paramInfos.Length != 2)
      return QuickError("Currently only events with 2 parameters (object & EventArgs) can be subscribed to.");

    int token = AssignCallbackToken();
    _callbackTokens[token] = new CancellationTokenSource();

    int clientPort = arg.RemoteEndPoint.Port;
    lock (_registeredPidsLock)
    {
      if (_clientCallbacks.TryGetValue(clientPort, out var tokens))
        tokens.Add(token);
    }

    EventHandler eventHandler = (obj, args) =>
    {
      DateTime timestamp = DateTime.Now;
      var eventKey = (resolvedType.FullName, eventName);
      if (!GlobalEvents.IsValidEvent(eventKey, obj, args, out var mappedArgs))
        return;

      _ = SyncThread.EnqueueAsync(
        async () => await InvokeControllerCallback(endpoint, token, timestamp, [obj, mappedArgs]),
        true,
        TimeSpan.FromSeconds(5));
    };

    try
    {
      Type eventArgsType = paramInfos[1].ParameterType;
      var wrapperType = typeof(EventWrapper<>).MakeGenericType(eventArgsType);
      var wrapperInstance = Activator.CreateInstance(wrapperType, eventHandler);
      Delegate my_delegate = Delegate.CreateDelegate(eventDelegateType, wrapperInstance, "Handle");

      Log.Debug($"[Diver] Adding event handler to event {eventName}...");
      eventObj.AddEventHandler(target, my_delegate);
      Log.Debug($"[Diver] Added event handler to event {eventName}!");

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
      return QuickError($"Failed insert the event handler: {ex}");
    }

    var erResults = new EventRegistrationResults { Token = token };
    return WrapSuccess(erResults);
  }
}
