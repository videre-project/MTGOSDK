/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;
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
  private static MethodInfo ResolveEventAccessor(
    EventInfo eventObj,
    Type targetType,
    bool isAdd)
  {
    var accessor = isAdd
      ? eventObj.GetAddMethod(nonPublic: true)
      : eventObj.GetRemoveMethod(nonPublic: true);

    if (accessor != null && !accessor.IsAbstract)
      return accessor;

    // Interface events can surface abstract accessors; map them to the
    // concrete implementation on the runtime target type.
    if (accessor?.DeclaringType?.IsInterface == true)
    {
      var map = targetType.GetInterfaceMap(accessor.DeclaringType);
      var idx = Array.IndexOf(map.InterfaceMethods, accessor);
      if (idx >= 0)
        return map.TargetMethods[idx];
    }

    return null;
  }

  private static void AddEventHandlerCompat(
    EventInfo eventObj,
    object target,
    Delegate handler)
  {
    var addMethod = ResolveEventAccessor(eventObj, target.GetType(), isAdd: true)
      ?? throw new InvalidOperationException(
        $"Cannot add handler for event '{eventObj.Name}' on type '{target.GetType().FullName}'.");

    addMethod.Invoke(target, [handler]);
  }

  private static void RemoveEventHandlerCompat(
    EventInfo eventObj,
    object target,
    Delegate handler)
  {
    var removeMethod = ResolveEventAccessor(eventObj, target.GetType(), isAdd: false)
      ?? throw new InvalidOperationException(
        $"Cannot remove handler for event '{eventObj.Name}' on type '{target.GetType().FullName}'.");

    removeMethod.Invoke(target, [handler]);
  }

  private static EventInfo ResolveEventInfo(Type resolvedType, string eventName)
  {
    const BindingFlags allInstanceEvents =
      BindingFlags.Instance |
      BindingFlags.Static |
      BindingFlags.Public |
      BindingFlags.NonPublic |
      BindingFlags.FlattenHierarchy;

    // Fast path for common public event lookup.
    var eventObj = resolvedType.GetEvent(eventName);
    if (eventObj != null)
      return eventObj;

    // Fallback for explicit/non-public event implementations.
    eventObj = resolvedType
      .GetEvents(allInstanceEvents)
      .FirstOrDefault(e =>
        string.Equals(e.Name, eventName, StringComparison.Ordinal) ||
        e.Name.EndsWith($".{eventName}", StringComparison.Ordinal));
    if (eventObj != null)
      return eventObj;

    // Final fallback: event declared on an implemented interface.
    foreach (var iface in resolvedType.GetInterfaces())
    {
      eventObj = iface.GetEvent(eventName);
      if (eventObj != null)
        return eventObj;

      eventObj = iface
        .GetEvents()
        .FirstOrDefault(e =>
          string.Equals(e.Name, eventName, StringComparison.Ordinal) ||
          e.Name.EndsWith($".{eventName}", StringComparison.Ordinal));
      if (eventObj != null)
        return eventObj;
    }

    return null;
  }

  private int _nextAvailableCallbackToken;

  public int AssignCallbackToken() =>
    Interlocked.Increment(ref _nextAvailableCallbackToken);

  //
  // Callback diagnostics
  //

  internal static long s_callbacksSent;
  internal static long s_lastCallbackQueueDelayTicks;

  /// <summary>
  /// Invokes a callback to the connected SDK client over TCP.
  /// </summary>
  public void InvokeCallback(
    int token,
    DateTime timestamp,
    params object[] parameters)
  {
    Interlocked.Increment(ref s_callbacksSent);
    var queueDelay = DateTime.Now - timestamp;
    Interlocked.Exchange(
      ref s_lastCallbackQueueDelayTicks, queueDelay.Ticks);

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

    EventInfo eventObj = ResolveEventInfo(resolvedType, eventName);
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
      AddEventHandlerCompat(eventObj, target, my_delegate);
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
