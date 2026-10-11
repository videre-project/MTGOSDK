/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Collections.Concurrent;

using MTGOSDK.Core.Memory;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.Core.Remoting.Types;

public class RemoteObject : IObjectReference
{
  private static readonly ActivitySource s_activitySource = new("MTGOSDK.Core");

  public void AddReference() => _ref?.AddReference();

  public void ReleaseReference(bool useJitter = false)
  {
    if (_isDisposed) return;
    _isDisposed = true;

    _ref?.ReleaseReference(useJitter);
  }

  /// <summary>
  /// Suppresses the remote unpin when disposing.
  /// </summary>
  internal void SuppressUnpin()
  {
    if (_isDisposed) return;
    _isDisposed = true;
    _ref?.SuppressUnpin();
  }

  public bool IsValid => _ref != null && _ref.IsValid && !_isDisposed;

  private bool _isDisposed = false;

  ~RemoteObject()
  {
    if (IsValid)
    {
      GCTimer.Enqueue(new QueuedRemoteObjectReference(_ref));
    }
  }

  private sealed class QueuedRemoteObjectReference(RemoteObjectRef reference) : IObjectReference
  {
    public bool IsValid => reference.IsValid;

    public void AddReference()
    { }

    public void ReleaseReference(bool useJitter) =>
      reference.ReleaseReference(useJitter);
  }

  private readonly RemoteHandle _app;
  private readonly RemoteObjectRef _ref;
  private Type _type = null;

  private readonly ConcurrentDictionary<Delegate, DiverCommunicator.LocalEventCallback> _eventCallbacksAndProxies = new();

  public ulong RemoteToken => _ref.Token;

  internal RemoteObject(RemoteObjectRef reference, RemoteHandle remoteApp)
  {
    // Increment the reference count of the remote object
    reference.AddReference();

    // Create a strong reference to the remote object
    _ref = reference;
    _app = remoteApp;
  }

  /// <summary>
  /// Gets the type of the proxied remote object, in the remote app. (This does not return `typeof(RemoteObject)`)
  /// </summary>
  public new Type GetType() => _type ??= _app.GetRemoteType(_ref.GetTypeDump());

  public ObjectOrRemoteAddress SetField(string fieldName, ObjectOrRemoteAddress newValue)
  {
    InvocationResults invokeRes = _ref.SetField(fieldName, newValue);
    return invokeRes.ReturnedObjectOrAddress;

  }

  public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(
    string methodName,
    params ObjectOrRemoteAddress[] args)
  => InvokeMethod(methodName, Array.Empty<string>(), args);

  public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(
    string methodName,
    string[] genericArgsFullTypeNames,
    params ObjectOrRemoteAddress[] args)
  {
    using var activity = s_activitySource.StartActivity("RO.InvokeMethod");
    activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
    activity?.SetTag("method", methodName);

    InvocationResults? invokeRes = _ref.InvokeMethod(methodName, genericArgsFullTypeNames, args);
    if (invokeRes is null)
    {
      throw new InvalidOperationException(
        $"Remote invocation '{GetType().FullName}.{methodName}' returned no result (null). " +
        "This usually indicates a diver/transport failure or an unexpected response format.");
    }
    if (invokeRes.VoidReturnType)
    {
      return (false, null);
    }
    return (true, invokeRes.ReturnedObjectOrAddress);
  }

  public dynamic Dynamify() => new DynamicRemoteObject(_app, this);

  public override string ToString()
  {
    return $"RemoteObject. Type: {_type?.FullName ?? "UNK"} Reference: [{_ref}]";
  }

  public ObjectOrRemoteAddress GetField(string name)
  {
    var res = _ref.GetField(name);
    return res.ReturnedObjectOrAddress;
  }

  public void EventSubscribe(string eventName, Delegate callback)
  {
    // TODO: Add a check for amount of parameters and types (need to be dynamics)
    // See implementation inside DynamicEventProxy

    void callbackProxy(HookContext context, ObjectOrRemoteAddress[] args)
    {
      DynamicRemoteObject[] droParameters = new DynamicRemoteObject[args.Length];
      for (int i = 0; i < args.Length; i++)
      {
        RemoteObject ro = _app.GetRemoteObjectFromField(args[i].RemoteAddress, args[i].Type);
        DynamicRemoteObject dro = ro.Dynamify() as DynamicRemoteObject;
        dro.__timestamp = args[i].Timestamp;

        droParameters[i] = dro;
      }

      // Call the callback with the proxied parameters (using DynamicRemoteObjects)
      callback.DynamicInvoke(droParameters);
    }

    RegisterEventCallback(eventName, callback, callbackProxy);
  }

  /// <summary>
  /// Subscribes to detached values captured in the native event handler or at
  /// an enclosing scope's completion. Delivery resolves no sender or argument handles.
  /// </summary>
  public RemoteCaptureSequence EventSubscribe(string eventName, Action<HookContext> callback,
    RemoteEventCapture capture, string[] propertyNames = null)
  {
    if (capture is null) throw new ArgumentNullException(nameof(capture));
    return RegisterEventCallback(eventName, callback, (context, _) => callback(context), capture, propertyNames);
  }

  private RemoteCaptureSequence RegisterEventCallback(string eventName, Delegate callback,
    DiverCommunicator.LocalEventCallback proxy, RemoteEventCapture capture = null,
    string[] propertyNames = null)
  {
    if (_eventCallbacksAndProxies.TryRemove(callback, out var existing))
      _ref.EventUnsubscribe(eventName, existing);
    var sequence = _ref.EventSubscribe(eventName, proxy, capture, propertyNames);
    _eventCallbacksAndProxies[callback] = proxy;
    return sequence;
  }

  internal void SetEventPropertyFilter(Delegate callback, string[] propertyNames)
  {
    if (!_eventCallbacksAndProxies.TryGetValue(callback, out var proxy))
      throw new InvalidOperationException("Event callback is not registered.");
    _ref.SetEventPropertyFilter(proxy, propertyNames);
  }

  public void EventUnsubscribe(string eventName, Delegate callback)
  {
    if (_eventCallbacksAndProxies.TryRemove(callback, out DiverCommunicator.LocalEventCallback callbackProxy))
    {
      _ref.EventUnsubscribe(eventName, callbackProxy);
    }
  }

  internal ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
  {
    return _ref.GetItem(key);
  }
}
