/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Reflection;
using System.Threading;

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

  /// <summary>
  /// Invokes a callback to the connected SDK client over TCP.
  /// </summary>
  public void InvokeCallback(
    int token,
    DateTime timestamp,
    params object[] parameters)
  {
    if (!_callbackTokens.TryGetValue(token, out var cts))
      return;

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
        ulong addr = _runtime.PinObject(parameter);
        int hashCode = parameter.GetHashCode();
        remoteParams[i] = ObjectOrRemoteAddress.FromToken(
          addr,
          parameter.GetType().FullName,
          hashCode);
      }
    }

    // Send callback over TCP
    var callbackRequest = new CallbackInvocationRequest
    {
      Token = token,
      Timestamp = timestamp,
      Parameters = [.. remoteParams]
    };
    SendTcpCallback(callbackRequest);
  }

  private byte[] MakeEventSubscribeResponse()
  {
    var request = DeserializeRequest<EventSubscriptionRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    ulong objAddr = request.Address;
    string eventName = request.EventName;

    Log.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

    if (!_runtime.TryGetPinnedObject(objAddr, out object target))
      return QuickError("Object at given address wasn't pinned (context: RegisterEventHandler)");

    Type resolvedType = target.GetType();

    if (string.IsNullOrEmpty(eventName))
      return QuickError("Missing parameter 'EventName'");

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

    EventHandler eventHandler = (obj, args) =>
    {
      DateTime timestamp = DateTime.Now;
      var eventKey = (resolvedType.FullName, eventName);
      if (!GlobalEvents.IsValidEvent(eventKey, obj, args, out var mappedArgs))
        return;

      SyncThread.Enqueue(() => InvokeCallback(token, timestamp, obj, mappedArgs));
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
        RegisteredProxy = my_delegate
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
