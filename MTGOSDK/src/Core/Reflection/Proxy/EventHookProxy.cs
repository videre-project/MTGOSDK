/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using System.Runtime.CompilerServices;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Hooking;


namespace MTGOSDK.Core.Reflection.Proxy;

public delegate (dynamic, dynamic)? EventHook(dynamic instance, dynamic[] args);
public delegate dynamic? EventValueHook(dynamic instance, dynamic[] args);

internal static class EventHookProxyHelpers
{
  public static T CastValue<T>(object? value)
  {
    if (value is T typedValue) return typedValue;
    if (value is null) return (T)value!;

    if (value is ITuple tuple &&
        TryCreateValueTuple(typeof(T), tuple, out object? converted))
      return (T)converted!;

    return (T)value;
  }

  private static bool TryCreateValueTuple(
    Type targetType,
    ITuple source,
    out object? result)
  {
    result = null;
    if (!targetType.IsGenericType ||
        !targetType.FullName!.StartsWith("System.ValueTuple`"))
      return false;

    Type[] targetTypes = targetType.GetGenericArguments();
    if (targetTypes.Length != source.Length) return false;

    object?[] values = new object?[targetTypes.Length];
    for (int i = 0; i < targetTypes.Length; i++)
    {
      object? value = source[i];
      if (value is null || targetTypes[i].IsInstanceOfType(value))
      {
        values[i] = value;
        continue;
      }

      if (value is not ITuple nested ||
          !TryCreateValueTuple(targetTypes[i], nested, out values[i]))
        return false;
    }

    result = Activator.CreateInstance(targetType, values);
    return result is not null;
  }

  public static ConstructorInfo GetConstructor(
    Type type,
    Type[] parameterTypes)
  {
    ConstructorInfo? constructor = type
      .GetConstructors(
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Instance)
      .SingleOrDefault(method =>
        method.GetParameters()
          .Select(parameter => parameter.ParameterType)
          .SequenceEqual(parameterTypes));

    return constructor
      ?? throw new MissingMethodException(
        type.FullName,
        $".ctor({string.Join(", ", parameterTypes.Select(t => t.Name))})");
  }
}

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
{
  // private event HookProxy<I,T> _eventHook;
  private event Action<I,T> _eventHook;

  private readonly string _typeName;
  private readonly string _methodName;
  private readonly MethodBase? _method;
  private readonly EventHook _hook;
  private readonly HarmonyPatchPosition _position;

  private readonly HookAction _hookAction;
  private int _initializedGeneration = -1;
  private readonly object _gate = new();

  // public delegate void HookProxy<I1, T1>(I1 instance, T1 args);

  public EventHookProxy(
    string typeName,
    string methodName,
    EventHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
  {
    this._typeName = typeName;
    this._methodName = methodName;
    this._method = null;
    this._hook = hook;
    this._position = position;

    this._hookAction = new((HookContext ctx, dynamic instance, dynamic[] args) =>
    {
      (dynamic, dynamic)? res = hook(instance, args);
      if ((object?)res is null) return; // Skip if the hook returns null.

      try
      {
        _eventHook?.Invoke(
          EventHookProxyHelpers.CastValue<I>((object?)res.Value.Item1),
          EventHookProxyHelpers.CastValue<T>((object?)res.Value.Item2));
      }
      catch (Exception e)
      {
        Log.Error("Error invoking event hook {0}: {1}", Name, e.Message);
        Log.Debug(e.StackTrace);
      }
    });
  }

  /// <summary>
  /// Creates a new instance of the <see cref="EventHookProxy{I, T}"/> class using a method group.
  /// </summary>
  /// <param name="typeName">The name of the type to hook.</param>
  /// <param name="method">The method to hook (method group).</param>
  /// <param name="hook">The hook function to call when the method is invoked.</param>
  /// <param name="position">The Harmony patch position for the hook.</param>
  public EventHookProxy(
    string typeName,
    MethodBase method,
    EventHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
  {
    this._typeName = typeName;
    this._methodName = method.Name;
    this._method = method;
    this._hook = hook;
    this._position = position;

    this._hookAction = new((HookContext ctx, dynamic instance, dynamic[] args) =>
    {
      (dynamic, dynamic)? res = hook(instance, args);
      if ((object?)res is null) return; // Skip if the hook returns null.

      try
      {
        _eventHook?.Invoke(
          EventHookProxyHelpers.CastValue<I>((object?)res.Value.Item1),
          EventHookProxyHelpers.CastValue<T>((object?)res.Value.Item2));
      }
      catch (Exception e)
      {
        Log.Error("Error invoking event hook {0}: {1}", Name, e.Message);
        Log.Debug(e.StackTrace);
      }
    });
  }

  /// <summary>
  /// Creates a new instance of the <see cref="EventHookProxy{I, T}"/> class
  /// that hooks a specific constructor.
  /// </summary>
  /// <param name="type">The type whose constructor should be hooked.</param>
  /// <param name="parameterTypes">
  /// The exact parameter types of the constructor to hook.
  /// </param>
  /// <param name="hook">The hook function to call when the constructor is invoked.</param>
  /// <param name="position">The Harmony patch position for the hook.</param>
  public EventHookProxy(
    Type type,
    Type[] parameterTypes,
    EventHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
    : this(
        type.FullName
          ?? throw new ArgumentException(
            "The constructor type must have a full name.",
            nameof(type)),
        EventHookProxyHelpers.GetConstructor(type, parameterTypes),
        hook,
        position)
  { }

  public void EnsureInitialize()
  {
    lock (_gate)
    {
      if (_initializedGeneration == -1)
      {
        DoInitialize();
      }

      // Verify the hook still exists even within the same remote connection.
      // Long-running MTGO sessions can invalidate remote hook state without a
      // full reconnect, while our managed subscribers remain attached.
      bool hasHook = _method == null
        ? RemoteClient.MethodHasHook(
            _typeName, _methodName, _hookAction, _position)
        : RemoteClient.MethodHasHook(_method, _hookAction, _position);
      if (!hasHook)
      {
        if (_method == null)
          RemoteClient.HookMethod(
            _typeName, _methodName, _hookAction, _position);
        else
          RemoteClient.HookMethod(_method, _hookAction, _position);
      }
      _initializedGeneration = RemoteClient.s_connectionGeneration;
    }
  }

  public override void Clear()
  {
    lock (_gate)
    {
      _eventHook = null;
      _initializedGeneration = -1;
      ResetInitialize();
      if (!RemoteClient.IsInitialized) return;

      if (_method == null)
        RemoteClient.UnhookMethod(_typeName, _methodName, _hookAction);
      else
        RemoteClient.UnhookMethod(_method, _hookAction);
    }
  }

  //
  // EventHandler wrapper methods.
  //

  public override string Name => _methodName;

  public static EventHookProxy<I,T> operator +(EventHookProxy<I,T> e, Delegate c)
  {
    lock (e._gate)
    {
      // e._eventHook += (HookProxy<I,T>)e.ProxyTypedDelegate(c);
      e._eventHook += (Action<I,T>)c;

      // If the method is not already hooked, hook it.
      e.EnsureInitialize();
    }

    return e;
  }

  public static EventHookProxy<I,T> operator -(EventHookProxy<I,T> e, Delegate c)
  {
    lock (e._gate)
    {
      // e._eventHook -= (HookProxy<I,T>)e.ProxyTypedDelegate(c);
      e._eventHook -= (Action<I,T>)c;

      // If there are no more subscribers, remove the hook.
      if (e._eventHook == null && RemoteClient.IsInitialized)
      {
        if (e._method == null)
          RemoteClient.UnhookMethod(
            e._typeName, e._methodName, e._hookAction);
        else
          RemoteClient.UnhookMethod(e._method, e._hookAction);
        e._initializedGeneration = -1;
        e.ResetInitialize();
      }
    }

    return e;
  }

  ~EventHookProxy()
  {
    if (_eventHook != null)
    {
      this.Clear();
    }
  }

  public static implicit operator EventHookProxy<dynamic, T>(
    EventHookProxy<I, T> e) =>
      e._method == null
        ? new EventHookProxy<dynamic, T>(
            e._typeName, e._methodName, e._hook, e._position)
        : new EventHookProxy<dynamic, T>(
            e._typeName, e._method, e._hook, e._position);
}

/// <summary>
/// A wrapper for hooking dynamic objects to create single-value custom events at runtime.
/// </summary>
/// <typeparam name="T">The type of the event value to wrap.</typeparam>
/// <remarks>
/// This class exposes a "+" and "-" operator overload for subscribing and
/// unsubscribing to events. This allows for a natural syntax for event
/// subscription and unsubscription.
/// </remarks>
public class EventHookProxy<T> : EventProxyBase<dynamic, T>
{
  private event Action<T> _eventHook;

  private readonly string _typeName;
  private readonly string _methodName;
  private readonly MethodBase? _method;
  private readonly EventValueHook _hook;
  private readonly HarmonyPatchPosition _position;

  private readonly HookAction _hookAction;
  private int _initializedGeneration = -1;
  private readonly object _gate = new();

  public EventHookProxy(
    string typeName,
    string methodName,
    EventValueHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
  {
    this._typeName = typeName;
    this._methodName = methodName;
    this._method = null;
    this._hook = hook;
    this._position = position;

    this._hookAction = new((HookContext ctx, dynamic instance, dynamic[] args) =>
    {
      dynamic? res = hook(instance, args);
      if ((object?)res is null) return; // Skip if the hook returns null.

      try
      {
        _eventHook?.Invoke(
          EventHookProxyHelpers.CastValue<T>((object?)res));
      }
      catch (Exception e)
      {
        Log.Error("Error invoking event hook {0}: {1}", Name, e.Message);
        Log.Debug(e.StackTrace);
      }
    });
  }

  /// <summary>
  /// Creates a new instance of the <see cref="EventHookProxy{T}"/> class using a method group.
  /// </summary>
  /// <param name="typeName">The name of the type to hook.</param>
  /// <param name="method">The method to hook (method group).</param>
  /// <param name="hook">The hook function to call when the method is invoked.</param>
  /// <param name="position">The Harmony patch position for the hook.</param>
  public EventHookProxy(
    string typeName,
    MethodBase method,
    EventValueHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
  {
    this._typeName = typeName;
    this._methodName = method.Name;
    this._method = method;
    this._hook = hook;
    this._position = position;

    this._hookAction = new((HookContext ctx, dynamic instance, dynamic[] args) =>
    {
      dynamic? res = hook(instance, args);
      if ((object?)res is null) return; // Skip if the hook returns null.

      try
      {
        _eventHook?.Invoke(
          EventHookProxyHelpers.CastValue<T>((object?)res));
      }
      catch (Exception e)
      {
        Log.Error("Error invoking event hook {0}: {1}", Name, e.Message);
        Log.Debug(e.StackTrace);
      }
    });
  }

  /// <summary>
  /// Creates a new instance of the <see cref="EventHookProxy{T}"/> class
  /// that hooks a specific constructor.
  /// </summary>
  /// <param name="type">The type whose constructor should be hooked.</param>
  /// <param name="parameterTypes">
  /// The exact parameter types of the constructor to hook.
  /// </param>
  /// <param name="hook">The hook function to call when the constructor is invoked.</param>
  /// <param name="position">The Harmony patch position for the hook.</param>
  public EventHookProxy(
    Type type,
    Type[] parameterTypes,
    EventValueHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
    : this(
        type.FullName
          ?? throw new ArgumentException(
            "The constructor type must have a full name.",
            nameof(type)),
        EventHookProxyHelpers.GetConstructor(type, parameterTypes),
        hook,
        position)
  { }

  public void EnsureInitialize()
  {
    lock (_gate)
    {
      if (_initializedGeneration == -1)
      {
        DoInitialize();
      }

      // Verify the hook still exists even within the same remote connection.
      // Long-running MTGO sessions can invalidate remote hook state without a
      // full reconnect, while our managed subscribers remain attached.
      bool hasHook = _method == null
        ? RemoteClient.MethodHasHook(
            _typeName, _methodName, _hookAction, _position)
        : RemoteClient.MethodHasHook(_method, _hookAction, _position);
      if (!hasHook)
      {
        if (_method == null)
          RemoteClient.HookMethod(
            _typeName, _methodName, _hookAction, _position);
        else
          RemoteClient.HookMethod(_method, _hookAction, _position);
      }
      _initializedGeneration = RemoteClient.s_connectionGeneration;
    }
  }

  public override void Clear()
  {
    lock (_gate)
    {
      _eventHook = null;
      _initializedGeneration = -1;
      ResetInitialize();
      if (!RemoteClient.IsInitialized) return;

      if (_method == null)
        RemoteClient.UnhookMethod(_typeName, _methodName, _hookAction);
      else
        RemoteClient.UnhookMethod(_method, _hookAction);
    }
  }

  //
  // EventHandler wrapper methods.
  //

  public override string Name => _methodName;

  public static EventHookProxy<T> operator +(EventHookProxy<T> e, Delegate c)
  {
    lock (e._gate)
    {
      e._eventHook += (Action<T>)c;

      // If the method is not already hooked, hook it.
      e.EnsureInitialize();
    }

    return e;
  }

  public static EventHookProxy<T> operator -(EventHookProxy<T> e, Delegate c)
  {
    lock (e._gate)
    {
      e._eventHook -= (Action<T>)c;

      // If there are no more subscribers, remove the hook.
      if (e._eventHook == null && RemoteClient.IsInitialized)
      {
        if (e._method == null)
          RemoteClient.UnhookMethod(
            e._typeName, e._methodName, e._hookAction);
        else
          RemoteClient.UnhookMethod(e._method, e._hookAction);
        e._initializedGeneration = -1;
        e.ResetInitialize();
      }
    }

    return e;
  }

  ~EventHookProxy()
  {
    if (_eventHook != null)
    {
      this.Clear();
    }
  }
}
