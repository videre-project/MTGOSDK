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

  /// <summary>
  /// Internal reference to the remote object handle.
  /// </summary>
  private dynamic @ro => Try(() => Unbind(@base).__ro, () => @base.__ro)
    ?? throw new InvalidOperationException(
        $"{type.Name} type does not implement DynamicRemoteObject.");

  private void EventSubscribe(string eventName, Delegate callback) =>
    @ro.EventSubscribe(eventName, callback);

  private void EventUnsubscribe(string eventName, Delegate callback) =>
    @ro.EventUnsubscribe(eventName, callback);

  //
  // EventHandler wrapper methods.
  //

  public string Name => name;

  public static EventProxy<I,T> operator +(EventProxy<I,T> e, Delegate c)
  {
    e.EventSubscribe(e.Name, e.ProxyTypedDelegate(c));
    return e;
  }

  public static EventProxy<I,T> operator -(EventProxy<I,T> e, Delegate c)
  {
    e.EventUnsubscribe(e.Name, e.ProxyTypedDelegate(c));
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
