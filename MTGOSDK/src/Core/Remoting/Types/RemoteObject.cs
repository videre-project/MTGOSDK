/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.Core.Remoting.Types;

public class RemoteObject
{
  private static readonly ConcurrentQueue<Tuple<WeakReference<RemoteObject>, RemoteObjectRef>> s_cleanupQueue = new();
  private static readonly Timer s_cleanupTimer;

  static RemoteObject()
  {
    // Run cleanup every 30 seconds
    s_cleanupTimer = new(CleanupCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
  }

  private static void CleanupCallback(object? state)
  {
    while (s_cleanupQueue.TryDequeue(out var item))
    {
      var weakRef = item.Item1;
      var remoteRef = item.Item2; // Get the ref associated with the weakRef

      if (weakRef.TryGetTarget(out _)) // Check if RemoteObject is still alive
      {
        // Object still alive, requeue for later check
        s_cleanupQueue.Enqueue(item);
      }
      else
      {
        // Object has been GC'd, release its reference count using the stored ref
        remoteRef?.ReleaseReference();
      }
    }
  }

  private bool _isDisposed = false;

  internal void AddReference() => _ref?.AddReference();

  internal void ReleaseReference(bool useJitter = false)
  {
    if (_isDisposed) return;
    _isDisposed = true;

    _ref?.ReleaseReference(useJitter);
  }

  internal bool IsValid => _ref != null && _ref.IsValid && !_isDisposed;

  ~RemoteObject()
  {
    if (_ref != null)
    {
      s_cleanupQueue.Enqueue(Tuple.Create(new WeakReference<RemoteObject>(this), _ref));
    }
  }

  private readonly RemoteHandle _app;
  private readonly RemoteObjectRef _ref;
  private Type _type = null;

  private readonly Dictionary<Delegate, DiverCommunicator.LocalEventCallback> _eventCallbacksAndProxies = new();

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
  => InvokeMethod(methodName, args);

  public (bool hasResults, ObjectOrRemoteAddress returnedValue) InvokeMethod(
    string methodName,
    string[] genericArgsFullTypeNames,
    params ObjectOrRemoteAddress[] args)
  {
    InvocationResults invokeRes = _ref.InvokeMethod(methodName, genericArgsFullTypeNames, args);
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

    void callbackProxy(ObjectOrRemoteAddress[] args)
    {
      DynamicRemoteObject[] droParameters = new DynamicRemoteObject[args.Length];
      for (int i = 0; i < args.Length; i++)
      {
        RemoteObject ro = _app.GetRemoteObject(args[i].RemoteAddress, args[i].Type);
        DynamicRemoteObject dro = ro.Dynamify() as DynamicRemoteObject;
        dro.__timestamp = args[i].Timestamp;

        droParameters[i] = dro;
      }

      // Call the callback with the proxied parameters (using DynamicRemoteObjects)
      callback.DynamicInvoke(droParameters);
    }

    _eventCallbacksAndProxies[callback] = callbackProxy;

    _ref.EventSubscribe(eventName, callbackProxy);
  }

  public void EventUnsubscribe(string eventName, Delegate callback)
  {
    if (_eventCallbacksAndProxies.TryGetValue(callback, out DiverCommunicator.LocalEventCallback callbackProxy))
    {
      _ref.EventUnsubscribe(eventName, callbackProxy);

      _eventCallbacksAndProxies.Remove(callback);
    }
  }

  internal ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
  {
    return _ref.GetItem(key);
  }
}
