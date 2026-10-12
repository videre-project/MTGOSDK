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

  // A RemoteObject is shared by every wrapper of its token that the cache hands
  // out: each holder takes one reference (the constructor counts the first)
  // and releases it once. The object releases its single share of the
  // RemoteObjectRef exactly once, when the last holder releases it or when it
  // is finalized, whichever comes first. (Releasing on the first holder's
  // release left the other holders' shares counted, so the ref was never
  // unpinned; releasing from both the finalizer and a holder would release
  // another live object's share.)
  // Holder acquisition and the final release are one transition under
  // _lifetimeLock, so a holder can never be added once the release is decided.
  private readonly object _lifetimeLock = new();
  private int _holders = 1;
  private bool _released;

  /// <summary>
  /// Takes one more holder; false once the object has been released (the
  /// caller must then obtain another object).
  /// </summary>
  internal bool TryAddReference()
  {
    lock (_lifetimeLock)
    {
      if (_released) return false;
      _holders++;
      return true;
    }
  }

  /// <summary>
  /// Creates a separate local owner of the same remote reference.
  /// The returned object can be released independently of this one.
  /// </summary>
  internal RemoteObject Retain()
  {
    lock (_lifetimeLock)
    {
      if (_released)
        throw new ObjectDisposedException(nameof(RemoteObject));
      return new RemoteObject(_ref, _app);
    }
  }

  public void AddReference() => TryAddReference();

  public void ReleaseReference(bool useJitter = false)
  {
    lock (_lifetimeLock)
    {
      if (_released || --_holders > 0) return;
      _released = true;
    }

    _ref?.ReleaseReference(useJitter);
  }

  /// <summary>
  /// Suppresses the remote unpin when disposing.
  /// </summary>
  internal void SuppressUnpin()
  {
    lock (_lifetimeLock)
    {
      if (_released) return;
      _released = true;
    }
    _ref?.SuppressUnpin();
  }

  public bool IsValid
  {
    get
    {
      lock (_lifetimeLock)
      {
        return !_released && _ref != null && _ref.IsValid;
      }
    }
  }

  ~RemoteObject()
  {
    // Unreachable, so no holder can release it any more: release the share
    // unless a holder already did.
    lock (_lifetimeLock)
    {
      if (_released) return;
      _released = true;
    }
    _ref?.ReleaseReference();
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
    try
    {
      InvocationResults invokeRes = _ref.SetField(fieldName, newValue);
      return invokeRes.ReturnedObjectOrAddress;
    }
    finally
    {
      GC.KeepAlive(this);
    }
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
    try
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
    finally
    {
      GC.KeepAlive(this);
    }
  }

  public dynamic Dynamify() => new DynamicRemoteObject(_app, this);

  public override string ToString()
  {
    return $"RemoteObject. Type: {_type?.FullName ?? "UNK"} Reference: [{_ref}]";
  }

  public ObjectOrRemoteAddress GetField(string name)
  {
    try
    {
      var res = _ref.GetField(name);
      return res.ReturnedObjectOrAddress;
    }
    finally
    {
      GC.KeepAlive(this);
    }
  }

  public void EventSubscribe(string eventName, Delegate callback)
  {
    // TODO: Add a check for amount of parameters and types (need to be dynamics)
    // See implementation inside DynamicEventProxy

    void callbackProxy(HookContext context, ObjectOrRemoteAddress[] args)
    {
      DynamicRemoteObject[] droParameters = new DynamicRemoteObject[args.Length];
      int resolved = 0;
      try
      {
        for (; resolved < args.Length; resolved++)
        {
          // Takes over this client's pin reference to the argument.
          RemoteObject ro = _app.GetRemoteObjectFromField(args[resolved], context.OwnsParameters);
          DynamicRemoteObject dro = ro.Dynamify() as DynamicRemoteObject;
          dro.__timestamp = args[resolved].Timestamp;

          droParameters[resolved] = dro;
        }
      }
      finally
      {
        // An argument left unresolved (after a failure) still holds a reference.
        if (resolved < args.Length)
          _app.Communicator.ReleaseCallbackArguments(context, args.Skip(resolved + 1));
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

  internal Task<RemoteCaptureSequence> RequestEventCaptureAsync(
    Delegate callback, bool forceUIThread)
  {
    if (!_eventCallbacksAndProxies.TryGetValue(callback, out var proxy))
      throw new InvalidOperationException("Event callback is not registered.");
    return _ref.RequestEventCaptureAsync(proxy, forceUIThread);
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
