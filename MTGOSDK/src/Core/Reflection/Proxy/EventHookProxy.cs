/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using System.Runtime.CompilerServices;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace MTGOSDK.Core.Reflection.Proxy;

public delegate (dynamic, dynamic)? EventHook(dynamic instance, dynamic[] args);
public delegate (dynamic, dynamic)? CapturedEventHook(HookContext context, dynamic instance, dynamic[] args);
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
  private event Action<I,T> _eventHook;

  private readonly string _typeName;
  private readonly string _methodName;
  private readonly MethodBase? _method;
  private readonly EventHook _hook;
  private readonly CapturedEventHook? _capturedHook;
  private readonly RemoteHookCapture? _capture;
  private readonly HarmonyPatchPosition _position;

  private readonly HookAction _hookAction;
  private readonly object _lock = new();

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

    this._hookAction = new((ctx, instance, args) =>
      Dispatch(ctx, (object?)instance, args));
  }

  /// <summary>
  /// Captures a caller-described batch in the target before queuing delivery.
  /// </summary>
  public EventHookProxy(string typeName, string methodName, CapturedEventHook hook,
    RemoteHookCapture capture, HarmonyPatchPosition position = HarmonyPatchPosition.Postfix)
  {
    _typeName = typeName;
    _methodName = methodName;
    _hook = null!;
    _capturedHook = hook;
    _capture = capture;
    _position = position;
    _hookAction = new((ctx, instance, args) => Dispatch(ctx, (object?)instance, args));
  }

  private void Dispatch(HookContext context, object? instance, dynamic[] args)
  {
    if (context.CaptureError is { } captureError)
    {
      // Ordered capture consumers must receive failures to advance their stream.
      if (context.CaptureSequence is null)
        throw new InvalidOperationException("Native hook capture failed: " + captureError.Message);
    }
    (dynamic, dynamic)? result = _capturedHook is null
      ? _hook(instance, args)
      : _capturedHook(context, instance, args);
    if (result is null)
    {
      return;
    }
    try
    {
      _eventHook?.Invoke(
        EventHookProxyHelpers.CastValue<I>((object?)result.Value.Item1),
        EventHookProxyHelpers.CastValue<T>((object?)result.Value.Item2));
    }
    catch (Exception error)
    {
      Log.Error(error, "Error invoking event hook {0}.", Name);
    }
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

    this._hookAction = new((ctx, instance, args) =>
      Dispatch(ctx, (object?)instance, args));
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
    lock (_lock)
    {
      DoInitialize();

      // Reinstall the hook if connection teardown cleared its registration.
      bool hasHook = _method == null
        ? RemoteClient.MethodHasHook(
            _typeName, _methodName, _hookAction, _position)
        : RemoteClient.MethodHasHook(_method, _hookAction, _position);
      if (!hasHook)
      {
        if (_method == null)
          RemoteClient.HookMethod(
            _typeName, _methodName, _hookAction, _position, _capture);
        else
          RemoteClient.HookMethod(_method, _hookAction, _position);
      }
    }
  }

  public override void Clear()
  {
    lock (_lock)
    {
      _eventHook = null;
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
    lock (e._lock)
    {
      e._eventHook += (Action<I,T>)c;

      // If the method is not already hooked, hook it.
      e.EnsureInitialize();
    }

    return e;
  }

  public static EventHookProxy<I,T> operator -(EventHookProxy<I,T> e, Delegate c)
  {
    lock (e._lock)
    {
      e._eventHook -= (Action<I,T>)c;

      // If there are no more subscribers, remove the hook.
      if (e._eventHook == null && RemoteClient.IsInitialized)
      {
        if (e._method == null)
          RemoteClient.UnhookMethod(
            e._typeName, e._methodName, e._hookAction);
        else
          RemoteClient.UnhookMethod(e._method, e._hookAction);
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
      e._capture != null
        ? new EventHookProxy<dynamic, T>(e._typeName, e._methodName,
            e._capturedHook!, e._capture, e._position)
        : e._method == null
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
  private readonly HarmonyPatchPosition _position;

  private readonly HookAction _hookAction;
  private readonly object _lock = new();

  public EventHookProxy(
    string typeName,
    string methodName,
    EventValueHook hook,
    HarmonyPatchPosition position = HarmonyPatchPosition.Prefix)
  {
    this._typeName = typeName;
    this._methodName = methodName;
    this._method = null;
    this._position = position;

    this._hookAction = new((ctx, instance, args) =>
      Dispatch(ctx, hook, (object?)instance, args));
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
    this._position = position;

    this._hookAction = new((ctx, instance, args) =>
      Dispatch(ctx, hook, (object?)instance, args));
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

  private void Dispatch(HookContext context, EventValueHook convert,
    object? instance, dynamic[] args)
  {
    dynamic? result = convert(instance, args);
    if ((object?)result is null)
    {
      return;
    }

    try
    {
      _eventHook?.Invoke(EventHookProxyHelpers.CastValue<T>((object?)result));
    }
    catch (Exception error)
    {
      Log.Error(error, "Error invoking event hook {0}.", Name);
    }
  }

  public void EnsureInitialize()
  {
    lock (_lock)
    {
      DoInitialize();

      // Reinstall the hook if connection teardown cleared its registration.
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
    }
  }

  public override void Clear()
  {
    lock (_lock)
    {
      _eventHook = null;
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
    lock (e._lock)
    {
      e._eventHook += (Action<T>)c;

      // If the method is not already hooked, hook it.
      e.EnsureInitialize();
    }

    return e;
  }

  public static EventHookProxy<T> operator -(EventHookProxy<T> e, Delegate c)
  {
    lock (e._lock)
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
