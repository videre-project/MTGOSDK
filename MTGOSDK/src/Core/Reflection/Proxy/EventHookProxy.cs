/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Hooking;


namespace MTGOSDK.Core.Reflection.Proxy;

public delegate (dynamic, dynamic)? EventHook(dynamic instance, dynamic[] args);

/// <summary>
/// A wrapper for hooking dynamic objects to create custom events at runtime.
/// </summary>
/// <typeparam name="I">The instance type of the sender to wrap.</typeparam>
/// <typeparam name="T">The type of the event arguments to wrap.</typeparam>
/// <remarks>
/// This class exposes a "+" and "-" operator overload for subscribing and
/// unsubscribing to events. This allows for a more natural syntax for event
/// subscription and unsubscription.
/// </remarks>
public class EventHookProxy<I, T> : EventProxyBase<I, T>
    where I : class
    where T : class
{
  // private event HookProxy<I,T> _eventHook;
  private event Action<I,T> _eventHook;

  private readonly string _typeName;
  private readonly string _methodName;
  private readonly EventHook _hook;

  private readonly HookAction _hookAction;

  // public delegate void HookProxy<I1, T1>(I1 instance, T1 args);

  public EventHookProxy(string typeName, string methodName, EventHook hook)
  {
    this._typeName = typeName;
    this._methodName = methodName;
    this._hook = hook;

    this._hookAction = new((HookContext ctx, dynamic instance, dynamic[] args) =>
    {
      (dynamic, dynamic)? res = hook(instance, args);
      if (res == null) return; // Skip if the hook returns null.

      try
      {
        _eventHook?.Invoke(res?.Item1, res?.Item2);
      }
      catch (Exception e)
      {
        Log.Error("Error invoking event hook {0}: {1}", Name, e.Message);
      }
    });
  }

  public void EnsureInitialize()
  {
    // If the method is not already hooked, hook it.
    if (!RemoteClient.MethodHasHook(_typeName, _methodName, _hookAction))
    {
      RemoteClient.HookMethod(_typeName, _methodName, _hookAction);
    }
  }

  //
  // EventHandler wrapper methods.
  //

  public override string Name => _methodName;

  public static EventHookProxy<I,T> operator +(EventHookProxy<I,T> e, Delegate c)
  {
    // e._eventHook += (HookProxy<I,T>)e.ProxyTypedDelegate(c);
    e._eventHook += (Action<I,T>)c;

    // If the method is not already hooked, hook it.
    e.EnsureInitialize();

    return e;
  }

  public static EventHookProxy<I,T> operator -(EventHookProxy<I,T> e, Delegate c)
  {
    // e._eventHook -= (HookProxy<I,T>)e.ProxyTypedDelegate(c);
    e._eventHook -= (Action<I,T>)c;

    // If there are no more subscribers, remove the hook.
    if (e._eventHook == null)
    {
      RemoteClient.UnhookMethod(e._typeName, e._methodName, e._hookAction);
    }

    return e;
  }

  ~EventHookProxy()
  {
    if (_eventHook != null)
    {
      RemoteClient.UnhookMethod(_typeName, _methodName, _hookAction);
      _eventHook = null;
    }
  }

  public static implicit operator EventHookProxy<dynamic, T>(EventHookProxy<I, T> e) =>
    new EventHookProxy<dynamic, T>(e._typeName, e._methodName, e._hook);
}
