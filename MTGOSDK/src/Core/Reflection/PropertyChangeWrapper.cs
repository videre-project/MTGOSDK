/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Event data for a tracked property value change.
/// </summary>
public sealed class PropertyValueChangedEventArgs<TValue>(
  string propertyPath,
  string propertyName,
  TValue oldValue,
  TValue newValue) : EventArgs
{
  /// <summary>
  /// Gets the logical property path being tracked.
  /// </summary>
  public string PropertyPath { get; } = propertyPath;

  /// <summary>
  /// Gets the concrete property name that triggered the notification.
  /// </summary>
  public string PropertyName { get; } = propertyName;

  /// <summary>
  /// Gets the previously observed value.
  /// </summary>
  public TValue OldValue { get; } = oldValue;

  /// <summary>
  /// Gets the current value after the change.
  /// </summary>
  public TValue NewValue { get; } = newValue;
}

internal interface IOwnerAwareEventProxy
{
  void AttachOwner(DLRWrapper owner);
}

/// <summary>
/// Watches a logical remote property path and exposes it like an SDK event.
/// </summary>
/// <remarks>
/// Nested paths such as <c>JoinedUsers.Count</c> automatically subscribe to
/// intermediate properties so downstream subscriptions are refreshed when the
/// object graph changes.
/// </remarks>
public sealed class PropertyChangeWrapper<DLRWrapper_T, TValue>
    : EventProxyBase<dynamic, PropertyValueChangedEventArgs<TValue>>,
      IOwnerAwareEventProxy where DLRWrapper_T : DLRWrapper
{
  private sealed record WatchDefinition(
    string[] TargetPath,
    string PropertyName,
    bool RefreshOnChange);

  private sealed record ActiveWatch(
    IPropertyChangeSubscription Subscription,
    EventHandler<RemotePropertyChangedEventArgs> Handler);

  private readonly string _name;
  private readonly Func<TValue>? _valueFactory;
  private readonly Func<DLRWrapper_T, TValue>? _ownerValueFactory;
  private readonly string _propertyPath;
  private readonly WatchDefinition[] _watchDefinitions;
  private readonly List<ActiveWatch> _activeWatches = [];
  private readonly object _gate = new();

  private DLRWrapper? _owner;
  private Action<PropertyValueChangedEventArgs<TValue>>? _instanceHandler;
  private TValue _lastValue = default!;
  private bool _hasLastValue;
  private bool _isTracking;

  public PropertyChangeWrapper(
    DLRWrapper? owner,
    string name,
    Func<TValue> valueFactory,
    string propertyPath)
  {
    _owner = owner;
    _name = name;
    _valueFactory = valueFactory
      ?? throw new ArgumentNullException(nameof(valueFactory));
    _propertyPath = string.IsNullOrWhiteSpace(propertyPath)
      ? throw new ArgumentException("Property path must not be empty.", nameof(propertyPath))
      : propertyPath;
    _watchDefinitions = BuildWatchDefinitions(propertyPath);
  }

  public PropertyChangeWrapper(
    DLRWrapper_T? owner,
    string name,
    string propertyPath,
    Func<DLRWrapper_T, TValue> valueFactory)
  {
    _owner = owner;
    _name = name;
    _ownerValueFactory = valueFactory
      ?? throw new ArgumentNullException(nameof(valueFactory));
    _propertyPath = string.IsNullOrWhiteSpace(propertyPath)
      ? throw new ArgumentException("Property path must not be empty.", nameof(propertyPath))
      : propertyPath;
    _watchDefinitions = BuildWatchDefinitions(propertyPath);
  }

  public PropertyChangeWrapper(
    string name,
    string propertyPath,
    Func<DLRWrapper, TValue> valueFactory)
      : this(null, name, propertyPath, valueFactory)
  { }

  public override string Name => _name;

  public override void Clear()
  {
    lock (_gate)
    {
      _instanceHandler = null;
      StopTrackingLocked();
    }
  }

  ~PropertyChangeWrapper() => Dispose();

  public static PropertyChangeWrapper<DLRWrapper_T, TValue> operator +(
    PropertyChangeWrapper<DLRWrapper_T, TValue> e,
    Delegate c)
  {
    lock (e._gate)
    {
      Action<PropertyValueChangedEventArgs<TValue>> handler =
        (Action<PropertyValueChangedEventArgs<TValue>>)c;
      bool shouldStartTracking = e._instanceHandler is null;

      e._instanceHandler += handler;

      if (shouldStartTracking)
      {
        try
        {
          e.DoInitialize();
          e.StartTrackingLocked();
        }
        catch
        {
          e._instanceHandler -= handler;
          throw;
        }
      }
    }

    return e;
  }

  public static PropertyChangeWrapper<DLRWrapper_T, TValue> operator -(
    PropertyChangeWrapper<DLRWrapper_T, TValue> e,
    Delegate c)
  {
    lock (e._gate)
    {
      e._instanceHandler -= (Action<PropertyValueChangedEventArgs<TValue>>)c;

      if (e._instanceHandler is null)
        e.StopTrackingLocked();
    }

    return e;
  }

  private void StartTrackingLocked()
  {
    if (_isTracking)
      return;

    _lastValue = ReadValue();
    _hasLastValue = true;
    RebuildSubscriptionsLocked();
    _isTracking = true;
  }

  private void StopTrackingLocked()
  {
    DisposeActiveWatchesLocked();
    _hasLastValue = false;
    _lastValue = default!;
    _isTracking = false;
  }

  private void RebuildSubscriptionsLocked()
  {
    DisposeActiveWatchesLocked();

    foreach (var definition in _watchDefinitions)
    {
      if (!TryResolveTarget(definition.TargetPath, out var target))
        continue;

      var subscription = PropertyChangeRouter.Shared.Subscribe(
        target,
        definition.PropertyName);

      EventHandler<RemotePropertyChangedEventArgs> handler = (_, args) =>
        OnTrackedPropertyChanged(definition, args);

      subscription.PropertyChanged += handler;
      _activeWatches.Add(new ActiveWatch(subscription, handler));
    }
  }

  private void DisposeActiveWatchesLocked()
  {
    foreach (var activeWatch in _activeWatches)
    {
      activeWatch.Subscription.PropertyChanged -= activeWatch.Handler;
      activeWatch.Subscription.Dispose();
    }

    _activeWatches.Clear();
  }

  private void OnTrackedPropertyChanged(
    WatchDefinition definition,
    RemotePropertyChangedEventArgs args)
  {
    Action<PropertyValueChangedEventArgs<TValue>>? handlers;
    PropertyValueChangedEventArgs<TValue> eventArgs;

    lock (_gate)
    {
      if (_instanceHandler is null)
        return;

      TValue oldValue = _hasLastValue ? _lastValue : ReadValue();

      if (definition.RefreshOnChange)
        RebuildSubscriptionsLocked();

      TValue newValue = ReadValue();
      _lastValue = newValue;
      _hasLastValue = true;

      handlers = _instanceHandler;
      eventArgs = new(_propertyPath, args.PropertyName, oldValue, newValue);
    }

    handlers?.Invoke(eventArgs);
  }

  private bool TryResolveTarget(
    string[] targetPath,
    out DynamicRemoteObject? target)
  {
    object? current = DLRWrapper.Unbind(GetOwner());

    foreach (var pathSegment in targetPath)
    {
      if (current is null ||
          !DynamicRemoteObject.TryGetDynamicMember(current, pathSegment, out var next) ||
          next is null)
      {
        target = null;
        return false;
      }

      current = DLRWrapper.Try(() => DLRWrapper.Unbind((dynamic)next), next);
    }

    target = current as DynamicRemoteObject
      ?? DLRWrapper.Try(() => DLRWrapper.Unbind((dynamic)current) as DynamicRemoteObject);

    return target is not null;
  }

  private DLRWrapper GetOwner()
  {
    if (_owner is not null)
      return _owner;

    _owner = TryResolveOwner(_ownerValueFactory?.Target ?? _valueFactory?.Target);
    return _owner ?? throw new InvalidOperationException(
      $"Unable to resolve the owning {nameof(DLRWrapper)} for property change wrapper '{Name}'. " +
      $"Pass an explicit owner or attach one from the containing wrapper constructor.");
  }

  void IOwnerAwareEventProxy.AttachOwner(DLRWrapper owner)
  {
    _owner ??= owner ?? throw new ArgumentNullException(nameof(owner));
  }

  private TValue ReadValue()
  {
    if (_ownerValueFactory is not null)
      return _ownerValueFactory(GetOwner() as DLRWrapper_T);

    if (_valueFactory is not null)
      return _valueFactory();

    throw new InvalidOperationException(
      $"Property change wrapper '{Name}' has no value factory.");
  }

  private static DLRWrapper? TryResolveOwner(object? candidate, int depth = 0)
  {
    if (candidate is null || depth > 3)
      return null;

    if (candidate is DLRWrapper owner)
      return owner;

    foreach (var field in candidate.GetType().GetFields(
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
    {
      object? value;
      try
      {
        value = field.GetValue(candidate);
      }
      catch
      {
        continue;
      }

      if (value is null || ReferenceEquals(value, candidate))
        continue;

      owner = TryResolveOwner(value, depth + 1);
      if (owner is not null)
        return owner;
    }

    return null;
  }

  private static WatchDefinition[] BuildWatchDefinitions(string propertyPath)
  {
    string[] segments = propertyPath
      .Split('.', StringSplitOptions.RemoveEmptyEntries)
      .Select(s => s.Trim())
      .Where(s => s.Length > 0)
      .ToArray();

    if (segments.Length == 0)
    {
      throw new ArgumentException(
        "Property path must contain at least one segment.",
        nameof(propertyPath));
    }

    List<WatchDefinition> definitions = [];

    for (int i = 0; i < segments.Length - 1; i++)
    {
      definitions.Add(new WatchDefinition(
        segments.Take(i).ToArray(),
        segments[i],
        RefreshOnChange: true));
    }

    definitions.Add(new WatchDefinition(
      segments.Take(segments.Length - 1).ToArray(),
      segments[^1],
      RefreshOnChange: false));

    return definitions.ToArray();
  }
}
