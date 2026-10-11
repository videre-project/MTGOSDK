/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

using MTGOSDK;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;


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

    // Resolve interface accessors to the runtime implementation because reflection
    // can return abstract add/remove methods for the interface event.
    if (accessor?.DeclaringType?.IsInterface == true)
    {
      var map = targetType.GetInterfaceMap(accessor.DeclaringType);
      var idx = Array.IndexOf(map.InterfaceMethods, accessor);
      if (idx >= 0)
        return map.TargetMethods[idx];
    }

    return null;
  }

  private static void AddEventHandler(
    EventInfo eventObj,
    object target,
    Delegate handler)
  {
    var addMethod = ResolveEventAccessor(eventObj, target.GetType(), isAdd: true)
      ?? throw new InvalidOperationException(
        $"Cannot add handler for event '{eventObj.Name}' on type '{target.GetType().FullName}'.");

    addMethod.Invoke(target, [handler]);
  }

  private static void RemoveEventHandler(
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
    const BindingFlags AllEvents =
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
      .GetEvents(AllEvents)
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

  private bool IsCallbackActive(int token) =>
    Volatile.Read(ref _disposed) == 0 && _activeCallbacks.ContainsKey(token);

  //
  // Callback diagnostics
  //

  internal static long s_callbacksSent;
  internal static long s_lastCallbackQueueDelayTicks;

  /// <summary>
  /// Invokes a callback to the connected SDK client over TCP.
  /// </summary>
  public void InvokeCallback(int token, DateTime timestamp, params object[] parameters) =>
    InvokeCallback(token, timestamp, null, null, null, parameters);

  private bool InvokeCallback(int token, DateTime timestamp,
    RemoteBatchResult capture, RemoteBatchError captureError,
    RemoteCaptureSequence sequence, params object[] parameters)
  {
    var queueDelay = DateTime.UtcNow - timestamp.ToUniversalTime();
    Interlocked.Exchange(
      ref s_lastCallbackQueueDelayTicks, queueDelay.Ticks);

    if (!IsCallbackActive(token))
      return false;

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
      Parameters = [.. remoteParams],
      Capture = capture,
      CaptureError = captureError,
      CaptureSequence = sequence
    };
    SendTcpCallback(callbackRequest);
    Interlocked.Increment(ref s_callbacksSent);
    return true;
  }

  // Parameters are omitted for captures that must release native arguments before
  // delivery. Ordinary callbacks retain those arguments until they are encoded.
  private void QueueCallback(int token, DateTime timestamp,
    RemoteBatchResult capture, RemoteBatchError error,
    RemoteCaptureSequence sequence, params object[] parameters) =>
    _callbackScheduler.Enqueue(() =>
      InvokeCallback(token, timestamp, capture, error, sequence, parameters));

  private RemoteBatchResult ExecuteCallbackCapture(RemoteBatchRequest batch,
    Func<IReadOnlyDictionary<int, object>> bindInputs, Exception nativeError,
    string source, out RemoteBatchError captureError)
  {
    captureError = null;
    if (nativeError != null)
    {
      // Preserve the native exception while reporting it to the subscriber.
      captureError = CreateCaptureError(nativeError);
      return null;
    }

    try
    {
      return _batchExecutor.Execute(batch, bindInputs());
    }
    catch (Exception error)
    {
      captureError = CreateCaptureError(error);
      Log.Error($"[Diver] Capture failed for {source}: {error}");
      return null;
    }
  }

  private static RemoteBatchError CreateCaptureError(Exception error) => new()
  {
    OperationId = -1,
    ExceptionType = error.GetType().FullName,
    Message = error.Message,
    StackTrace = error.ToString()
  };

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

    if (request.Capture is { } description)
    {
      if (description.Batch == null || description.Batch.ForceUIThread ||
          description.Batch.Inputs == null || description.Inputs == null || description.Inputs.Any(binding =>
            binding.Key < 0 || binding.Key >= description.Batch.Inputs.Count ||
            binding.Value < 0 || binding.Value >= paramInfos.Length))
        return QuickError("Invalid event capture inputs; capture runs on the native event thread.");
      try
      {
        BatchExecutor.Validate(description.Batch);
        CaptureScopeManager.Validate(description.Scope, description.Batch);
        if (description.Scope != null && description.Inputs.ContainsKey(description.Scope.OwnerInput))
          throw new ArgumentException("Event scope owner must be a retained batch input.");
      }
      catch (Exception error)
      {
        return QuickError("Invalid event capture: " + error.Message);
      }
    }

    if (request.PropertyNames is not null &&
        !typeof(PropertyChangedEventArgs).IsAssignableFrom(paramInfos[1].ParameterType))
      return QuickError("Property filters require PropertyChangedEventArgs.");

    var registration = new RegisteredEventHandlerInfo
    {
      EventInfo = eventObj,
      Target = target,
      PropertyNames = NormalizePropertyNames(request.PropertyNames)
    };

    object scopeOwner = null;
    RemoteCaptureSequence baseline = null;
    if (request.Capture?.Scope is { } scope)
    {
      try
      {
        var captureRoot = _runtime.ParseParameterObject(request.Capture.Batch.Inputs[scope.OwnerInput]);
        scopeOwner = captureRoot;
        if (scope.OwnerPath != null)
          scopeOwner = _batchExecutor.Read(scopeOwner, scope.OwnerPath);
        registration.CaptureScopeSubscription = _captureScopes.Subscribe(scope.Group, scopeOwner, out baseline, captureRoot);
      }
      catch (Exception error)
      {
        return QuickError("Invalid event scope owner: " + error.Message);
      }
    }

    int token = AssignCallbackToken();
    _activeCallbacks[token] = 0;

    EventHandler eventHandler = (obj, args) =>
    {
      if (!IsCallbackActive(token))
        return;
      DateTime timestamp = DateTime.UtcNow;
      var eventKey = (resolvedType.FullName, eventName);
      if (!GlobalEvents.IsValidEvent(eventKey, obj, args, out var mappedArgs))
        return;

      // Filter on the native notification before reflection, serialization or queueing.
      var names = Volatile.Read(ref registration.PropertyNames);
      if (names is not null && (names.Length == 0 ||
          (args is PropertyChangedEventArgs change && !string.IsNullOrEmpty(change.PropertyName) &&
           Array.IndexOf(names, change.PropertyName) < 0)))
        return;

      void Complete(RemoteCaptureSequence sequence, Exception nativeError = null)
      {
        if (!IsCallbackActive(token))
          return;

        // An enclosing scope calls this after the native update completes.
        // Otherwise, the event handler captures immediately. In both cases the
        // values are detached before callback delivery can be delayed.
        RemoteBatchResult captured = null;
        RemoteBatchError captureError = null;
        if (request.Capture is { } capture)
        {
          captured = ExecuteCallbackCapture(capture.Batch, () =>
          {
            object[] parameters = [obj, args];
            return capture.Inputs.ToDictionary(binding => binding.Key,
              binding => parameters[binding.Value]);
          }, nativeError, $"event {resolvedType.FullName}.{eventName}", out captureError);
        }

        if (request.Capture == null)
          // Cast mappedArgs to keep this void invocation statically bound.
          // Otherwise, the scheduler can treat it as a task-returning callback.
          _callbackScheduler.Enqueue(() => InvokeCallback(token, timestamp, obj, (object)mappedArgs));
        else
          QueueCallback(token, timestamp, captured, captureError, sequence);
      }

      if (request.Capture?.Scope is { } scope)
      {
        if (_captureScopes.MarkChanged(scope.Group, scopeOwner, eventName, Complete, objAddr))
          return;
        _captureScopes.Complete(scope.Group, scopeOwner, sequence =>
        {
          sequence.EventNames = [eventName];
          sequence.SourceToken = objAddr;
          sequence.EventSources[eventName] = [objAddr];
          Complete(sequence);
        });
      }
      else Complete(null);
    };

    try
    {
      Type eventArgsType = paramInfos[1].ParameterType;
      var wrapperType = typeof(EventWrapper<>).MakeGenericType(eventArgsType);
      var wrapperInstance = Activator.CreateInstance(wrapperType, eventHandler);
      Delegate handler = Delegate.CreateDelegate(eventDelegateType, wrapperInstance, "Handle");

      Log.Debug($"[Diver] Adding event handler to event {eventName}...");
      AddEventHandler(eventObj, target, handler);
      Log.Debug($"[Diver] Added event handler to event {eventName}!");

      registration.RegisteredProxy = handler;
      _remoteEventHandler[token] = registration;
    }
    catch (Exception ex)
    {
      registration.CaptureScopeSubscription?.Dispose();
      _activeCallbacks.TryRemove(token, out _);
      return QuickError($"Failed insert the event handler: {ex}");
    }

    var erResults = new EventRegistrationResults { Token = token, CaptureSequence = baseline };
    return WrapSuccess(erResults);
  }

  private static string[] NormalizePropertyNames(string[] names) => names?.Where(
    name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();

  private byte[] MakeEventPropertyFilterResponse()
  {
    var request = DeserializeRequest<EventPropertyFilterRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");
    if (!_remoteEventHandler.TryGetValue(request.Token, out var registration))
      return QuickError("Unknown token for event callback subscription");
    var arguments = registration.EventInfo.EventHandlerType.GetMethod("Invoke").GetParameters();
    if (!typeof(PropertyChangedEventArgs).IsAssignableFrom(arguments[1].ParameterType))
      return QuickError("Property filters require PropertyChangedEventArgs.");

    Volatile.Write(ref registration.PropertyNames, NormalizePropertyNames(request.PropertyNames));
    return s_okResponse;
  }
}
