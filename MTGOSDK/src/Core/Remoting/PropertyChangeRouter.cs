/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.ComponentModel;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting;

public interface IPropertyChangeSubscription : IDisposable
{
  ulong Token { get; }

  /// <summary>
  /// Native capture stream baseline supplied at subscription time.
  /// </summary>
  RemoteCaptureSequence? CaptureSequence { get; }

  IReadOnlyCollection<string> TrackedProperties { get; }

  void AddTrackedProperties(params string[] propertyNames);

  bool RemoveTrackedProperties(params string[] propertyNames);

  void SetTrackedProperties(params string[] propertyNames);

  event EventHandler<RemotePropertyChangedEventArgs>? PropertyChanged;
}

/// <summary>
/// Shares a native property subscription among local observers with the same source
/// and capture description. The native handler filters by all requested properties,
/// then local delivery applies each observer's own filter.
/// </summary>
public sealed class PropertyChangeRouter : IDisposable
{
  private sealed record TokenRegistration(
    RemotePropertyChangeProxy Proxy,
    PropertyChangedEventHandler Handler,
    Dictionary<Guid, PropertyChangeSubscription> Subscriptions);

  private sealed class PropertyChangeSubscription(
    PropertyChangeRouter router,
    ulong token,
    Guid id,
    IEnumerable<string> initialProperties,
    string captureKey) : IPropertyChangeSubscription
  {
    private readonly PropertyChangeRouter _router = router;
    private readonly Guid _id = id;
    private readonly HashSet<string> _trackedProperties = new(
      initialProperties.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _isDisposed;

    public ulong Token { get; } = token;

    public RemoteCaptureSequence? CaptureSequence { get; internal set; }

    public IReadOnlyCollection<string> TrackedProperties
    {
      get
      {
        lock (_gate)
          return _trackedProperties.ToArray();
      }
    }

    public event EventHandler<RemotePropertyChangedEventArgs>? PropertyChanged;

    public void AddTrackedProperties(params string[] propertyNames)
    {
      if (propertyNames is null) throw new ArgumentNullException(nameof(propertyNames));
      ChangeTrackedProperties(properties => properties.UnionWith(
        propertyNames.Where(name => !string.IsNullOrWhiteSpace(name))));
    }

    public bool RemoveTrackedProperties(params string[] propertyNames)
    {
      if (propertyNames is null) throw new ArgumentNullException(nameof(propertyNames));
      bool removed = false;
      ChangeTrackedProperties(properties =>
      {
        foreach (string name in propertyNames)
          if (!string.IsNullOrWhiteSpace(name)) removed |= properties.Remove(name);
      });
      return removed;
    }

    public void SetTrackedProperties(params string[] propertyNames)
    {
      if (propertyNames is null) throw new ArgumentNullException(nameof(propertyNames));
      ChangeTrackedProperties(properties =>
      {
        properties.Clear();
        properties.UnionWith(propertyNames.Where(name => !string.IsNullOrWhiteSpace(name)));
      });
    }

    private void ChangeTrackedProperties(Action<HashSet<string>> change)
    {
      lock (_router._gate)
      {
        ThrowIfDisposed();
        string[] previous;
        lock (_gate)
        {
          previous = _trackedProperties.ToArray();
          change(_trackedProperties);
        }
        try { _router.RefreshPropertyFilter((Token, captureKey)); }
        catch
        {
          lock (_gate)
          {
            _trackedProperties.Clear();
            _trackedProperties.UnionWith(previous);
          }
          throw;
        }
      }
    }

    internal bool Matches(string propertyName)
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        return false;

      lock (_gate)
        return _trackedProperties.Count != 0 &&
          (string.IsNullOrEmpty(propertyName) || _trackedProperties.Contains(propertyName));
    }

    internal void Raise(RemotePropertyChangedEventArgs args)
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        return;

      PropertyChanged?.Invoke(this, args);
    }

    internal void MarkDisposed()
    {
      if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        return;

      PropertyChanged = null;
      lock (_gate)
        _trackedProperties.Clear();
    }

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        return;

      _router.RemoveSubscription((Token, captureKey), _id);

      PropertyChanged = null;
      lock (_gate)
        _trackedProperties.Clear();
    }

    private void ThrowIfDisposed()
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        throw new ObjectDisposedException(nameof(PropertyChangeSubscription));
    }
  }

  public static PropertyChangeRouter Shared { get; } = new();

  private readonly ConcurrentDictionary<(ulong Token, string Capture), TokenRegistration> _registrations = new();
  private readonly object _gate = new();
  private int _isDisposed;

  public event EventHandler<RemotePropertyChangedEventArgs>? PropertyChanged;

  public int Count => _registrations.Keys.Select(key => key.Token).Distinct().Count();

  public PropertyChangeRouter()
  {
    RemoteClient.Disposed += OnRemoteClientDisposed;
  }

  public IPropertyChangeSubscription Subscribe(
    DynamicRemoteObject dro,
    params string[] propertyNames) => Subscribe(dro, null, propertyNames);

  /// <summary>
  /// Captures values inside matching native PropertyChanged notifications.
  /// Subscriptions sharing this capture description combine their requested
  /// properties for native filtering and retain their own filters for local delivery.
  /// </summary>
  public IPropertyChangeSubscription Subscribe(DynamicRemoteObject dro,
    RemoteBatch? capture, params string[] propertyNames) =>
    Subscribe(dro, capture, null, null, propertyNames);

  /// <summary>
  /// Enrolls notification capture in a caller-described native scope, deferring
  /// projection until the enclosing invocation completes. Notifications outside
  /// a scope capture immediately. The callback is installed before the filter is activated.
  /// </summary>
  public IPropertyChangeSubscription Subscribe(DynamicRemoteObject dro,
    RemoteBatch? capture, RemoteCaptureScope? scope,
    EventHandler<RemotePropertyChangedEventArgs>? callback, params string[] propertyNames)
  {
    if (scope is not null && (capture is null || callback is null))
      throw new ArgumentException("Scoped subscriptions require a capture and an installed callback.");
    if (Volatile.Read(ref _isDisposed) != 0)
      throw new ObjectDisposedException(nameof(PropertyChangeRouter));
    if (dro is null)
      throw new ArgumentNullException(nameof(dro));

    var remoteObject = dro.__ro
      ?? throw new ArgumentException(
        "DynamicRemoteObject has no backing remote object.",
        nameof(dro));

    ulong token = remoteObject.RemoteToken;
    string captureKey = capture is null ? ""
      : Convert.ToBase64String(MessagePackSerializer.Serialize(
          new RemoteEventCapture { Batch = capture.ToRequest(), Scope = scope }));
    var key = (Token: token, Capture: captureKey);
    var subscriptionId = Guid.NewGuid();
    var subscription = new PropertyChangeSubscription(
      this,
      token,
      subscriptionId,
      propertyNames ?? [], captureKey);
    if (callback is not null) subscription.PropertyChanged += callback;

    lock (_gate)
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        throw new ObjectDisposedException(nameof(PropertyChangeRouter));

      bool shared = _registrations.TryGetValue(key, out var registration);
      if (!shared)
      {
        var proxy = new RemotePropertyChangeProxy(remoteObject, capture,
          subscription.TrackedProperties.OrderBy(name => name, StringComparer.Ordinal).ToArray(), scope);

        PropertyChangedEventHandler handler = (_, args) =>
          OnProxyPropertyChanged(key, proxy, args);

        proxy.PropertyChanged += handler;

        registration = new TokenRegistration(
          proxy,
          handler,
          new Dictionary<Guid, PropertyChangeSubscription>());

        _registrations[key] = registration;
      }

      subscription.CaptureSequence = registration!.Proxy.CaptureSequence;
      registration.Subscriptions[subscriptionId] = subscription;
      if (shared || scope is not null)
      {
        // Keep the native filter empty until local routing and the stream baseline
        // are installed so notifications cannot arrive before the subscriber is ready.
        try { RefreshPropertyFilter(key); }
        catch
        {
          registration.Subscriptions.Remove(subscriptionId);
          subscription.MarkDisposed();
          if (!shared)
          {
            _registrations.TryRemove(key, out _);
            try { DisposeRegistration(registration); }
            catch (Exception error)
            {
              Log.Debug(error, "Failed to release inactive property subscription for token={Token}", token);
            }
          }
          throw;
        }
      }
    }

    return subscription;
  }

  public bool Unsubscribe(ulong token)
  {
    if (token == 0 || Volatile.Read(ref _isDisposed) != 0)
      return false;

    List<TokenRegistration> removed = [];
    lock (_gate)
    {
      foreach (var key in _registrations.Keys.Where(key => key.Token == token))
        if (_registrations.TryRemove(key, out var existing)) removed.Add(existing);
    }
    foreach (var registration in removed) DisposeRegistration(registration);
    return removed.Count != 0;
  }

  public void Clear()
  {
    KeyValuePair<(ulong Token, string Capture), TokenRegistration>[] entries;
    lock (_gate)
    {
      entries = _registrations.ToArray();
      _registrations.Clear();
    }

    foreach (var entry in entries)
      DisposeRegistration(entry.Value);
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
      return;

    RemoteClient.Disposed -= OnRemoteClientDisposed;
    Clear();
    GC.SuppressFinalize(this);
  }

  private void OnRemoteClientDisposed(object? sender, EventArgs args)
  {
    // Remote proxies are tied to the active transport and should be torn down
    // eagerly on dispose to avoid stale callback plumbing between connections.
    Clear();
  }

  private void OnProxyPropertyChanged(
    (ulong Token, string Capture) key,
    INotifyPropertyChanged source,
    PropertyChangedEventArgs args)
  {
    if (Volatile.Read(ref _isDisposed) != 0)
      return;

    var propertyName = args?.PropertyName;
    var capturedArgs = args as RemotePropertyChangedEventArgs;
    var context = capturedArgs?.CaptureContext;

    List<PropertyChangeSubscription> matchingSubscriptions = [];
    lock (_gate)
    {
      if (!_registrations.TryGetValue(key, out var registration))
        return;

      foreach (var subscription in registration.Subscriptions.Values)
      {
        if (context?.CaptureError is not null || subscription.Matches(propertyName ?? ""))
          matchingSubscriptions.Add(subscription);
      }
    }

    if (matchingSubscriptions.Count == 0 && PropertyChanged is null)
      return;

    var eventArgs = capturedArgs ?? new RemotePropertyChangedEventArgs(key.Token, propertyName ?? "", source);
    var groupId = $"token-pc-router:{key.Token}";

    SyncThread.Enqueue(() =>
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        return;

      PropertyChanged?.Invoke(this, eventArgs);

      foreach (var subscription in matchingSubscriptions)
        subscription.Raise(eventArgs);
    }, groupId);
  }

  // The caller holds _gate while replacing the union of active property filters.
  private void RefreshPropertyFilter((ulong Token, string Capture) key)
  {
    if (!_registrations.TryGetValue(key, out var registration)) return;
    registration.Proxy.SetTrackedProperties(registration.Subscriptions.Values
      .SelectMany(subscription => subscription.TrackedProperties)
      .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray());
  }

  private void RemoveSubscription((ulong Token, string Capture) key, Guid id)
  {
    TokenRegistration? toDispose = null;

    lock (_gate)
    {
      if (!_registrations.TryGetValue(key, out var registration))
        return;

      if (!registration.Subscriptions.TryGetValue(id, out var subscription))
        return;

      registration.Subscriptions.Remove(id);
      subscription.MarkDisposed();

      if (registration.Subscriptions.Count == 0)
      {
        _registrations.TryRemove(key, out _);
        toDispose = registration;
      }
      else
      {
        // Failure can leave the native filter wider than necessary. Local matching
        // still uses the current subscriptions, so the extra notifications are filtered.
        try { RefreshPropertyFilter(key); }
        catch (Exception error) { Log.Debug(error, "Failed to narrow property filter for token={Token}", key.Token); }
      }
    }

    if (toDispose is not null)
      DisposeRegistration(toDispose);
  }

  private static void DisposeRegistration(TokenRegistration registration)
  {
    registration.Proxy.PropertyChanged -= registration.Handler;

    foreach (var subscription in registration.Subscriptions.Values)
      subscription.MarkDisposed();

    registration.Subscriptions.Clear();
    registration.Proxy.Dispose();
  }
}