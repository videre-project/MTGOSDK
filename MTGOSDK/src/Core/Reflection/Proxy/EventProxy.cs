/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Proxy;

/// <summary>
/// A wrapper for dynamic objects that implement events at runtime.
/// </summary>
/// <typeparam name="I">The instance type of the sender to wrap.</typeparam>
/// <typeparam name="T">The type of the event arguments to wrap.</typeparam>
/// <remarks>
/// This class exposes a "+" and "-" operator overload for subscribing and
/// unsubscribing to events. This allows for a more natural syntax for event
/// subscription and unsubscription.
/// </remarks>
public class EventProxy<I, T>(dynamic @ref, string name) : EventProxyBase<I, T>
    where I : class
    where T : class
{
  /// <summary>
 	/// Stores an internal reference to the eventhandler instance.
 	/// </summary>
  internal override dynamic obj => Unbind(@ref);

  private void EventSubscribe(string eventName, Delegate callback) =>
    @ro.EventSubscribe(eventName, callback);

  private void EventUnsubscribe(string eventName, Delegate callback) =>
    @ro.EventUnsubscribe(eventName, callback);

  private readonly List<(Delegate Original, Delegate Proxy)> _delegates = new();

  public override void Clear()
  {
    var proxies = _delegates.Select(d => d.Proxy).ToArray();
    _delegates.Clear();

    Exception? failure = null;
    foreach (var d in proxies)
    {
      try
      {
        EventUnsubscribe(Name, d);
      }
      catch (Exception ex)
      {
        failure ??= ex;
      }
    }

    if (failure is not null)
      throw failure;
  }

  //
  // EventHandler wrapper methods.
  //

  public override string Name => name;

  private bool _isInitialized;

  public static EventProxy<I,T> operator +(EventProxy<I,T> e, Delegate c)
  {
    e.DoInitialize();
    var d = e.ProxyTypedDelegate(c);
    e.EventSubscribe(e.Name, d);
    e._delegates.Add((c, d));
    return e;
  }

  public static EventProxy<I,T> operator -(EventProxy<I,T> e, Delegate c)
  {
    var index = e._delegates.FindIndex(d => d.Original == c);
    if (index < 0)
      return e;

    var d = e._delegates[index].Proxy;
    e._delegates.RemoveAt(index);
    e.EventUnsubscribe(e.Name, d);
    return e;
  }

  //
  // EventProxy type conversion operators.
  //

  public static implicit operator EventProxy<T>(EventProxy<I,T> e) =>
    new(e.obj, e.Name);

  public static implicit operator EventProxy(EventProxy<I,T> e) =>
    new(e.obj, e.Name);
}

/// <summary>
/// A wrapper for dynamic objects that implement events at runtime.
/// </summary>
/// <typeparam name="T">The type of the event arguments to wrap.</typeparam>
/// <remarks>
/// This class exposes a "+" and "-" operator overload for subscribing and
/// unsubscribing to events. This allows for a more natural syntax for event
/// subscription and unsubscription.
/// </remarks>
public class EventProxy<T>(dynamic @ref, string name)
    : EventProxy<dynamic, T>(null, name) where T : class
{
  internal override dynamic obj => @ref;//Unbind(@ref);
}

/// <summary>
/// A wrapper for dynamic objects that implement events at runtime.
/// </summary>
/// <remarks>
/// This class exposes a "+" and "-" operator overload for subscribing and
/// unsubscribing to events. This allows for a more natural syntax for event
/// subscription and unsubscription.
/// </remarks>
public class EventProxy(dynamic @ref, string name)
    : EventProxy<dynamic>(null, name)
{
  internal override dynamic obj => Unbind(@ref);
}
