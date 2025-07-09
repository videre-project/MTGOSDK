/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection;

public delegate bool Filter<I>(dynamic t, I i);

public class EventHookWrapper<I>(
  EventHookProxy<dynamic, I> handler,
  Filter<I> hook)
    : EventProxyBase<dynamic, I>
{
  private EventHookProxy<dynamic, I> _handler = handler;
  private event Action<I> _instanceHandler = null;

  private readonly Filter<I> _hook = hook;
  private Action<dynamic, I> _instanceHook = null;

  public override void Clear()
  {
    _instanceHandler = null;
    _handler -= _instanceHook;
  }

  ~EventHookWrapper() => Clear();

  public static EventHookWrapper<I> operator +(EventHookWrapper<I> e, Delegate c)
  {
    if (e._instanceHandler == null)
    {
      e._instanceHook ??= new Action<dynamic, I>((t, i) =>
      {
        if (!e._hook(t, i)) return;
        e._instanceHandler?.Invoke(i);
      });
      e._handler += e._instanceHook;
    }

    e._instanceHandler += (Action<I>)c;
    return e;
  }

  public static EventHookWrapper<I> operator -(EventHookWrapper<I> e, Delegate c)
  {
    e._instanceHandler -= (Action<I>)c;

    if (e._instanceHandler == null)
    {
      e._handler -= e._instanceHook;
    }

    return e;
  }
}
