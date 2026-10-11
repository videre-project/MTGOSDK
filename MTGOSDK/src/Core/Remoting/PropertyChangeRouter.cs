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
  private sealed class TokenRegistration((ulong Token, string Capture) key)
  {
    public (ulong Token, string Capture) Key { get; } = key;

    // Serialize native registration, filter changes, and disposal for this registration.
    // Notification routing uses the router lock and never waits for this lock.
    public object UpdateLock { get; } = new();

    public RemotePropertyChangeProxy? Proxy { get; set; }

    public PropertyChangedEventHandler? Handler { get; set; }

    public Dictionary<Guid, PropertyChangeSubscription> Subscriptions { get; } = new();
  }

  private sealed class PropertyChangeSubscription(
    PropertyChangeRouter router,
    ulong token,
    Guid id,
    IEnumerable<string> initialProperties,
    TokenRegistration registration) : IPropertyChangeSubscription
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
      lock (registration.UpdateLock)
      {
        string[] previous;
        lock (_router._gate)
        {
          ThrowIfDisposed();
          _router.ThrowIfDetached(registration);
          lock (_gate)
          {
            previous = _trackedProperties.ToArray();
            change(_trackedProperties);
          }
        }

        try
        {
          _router.RefreshPropertyFilter(registration);
        }
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

      _router.RemoveSubscription(registration, _id);

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
    TokenRegistration registration;
    lock (_gate)
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        throw new ObjectDisposedException(nameof(PropertyChangeRouter));
      registration = _registrations.GetOrAdd(key, value => new TokenRegistration(value));
    }

    var subscriptionId = Guid.NewGuid();
    var subscription = new PropertyChangeSubscription(
      this, token, subscriptionId, propertyNames ?? [], registration);
    if (callback is not null)
      subscription.PropertyChanged += callback;

    lock (registration.UpdateLock)
    {
      try
      {
        lock (_gate)
          ThrowIfDetached(registration);

        if (registration.Proxy is null)
        {
          // Captures start with an empty filter. Activate it only after local routing
          // and the subscription's stream baseline have been installed.
          var proxy = new RemotePropertyChangeProxy(remoteObject, capture,
            capture is null ? null : Array.Empty<string>(), scope);
          registration.Handler = (_, args) => OnProxyPropertyChanged(registration, proxy, args);
          proxy.PropertyChanged += registration.Handler;
          registration.Proxy = proxy;
        }

        lock (_gate)
        {
          ThrowIfDetached(registration);
          subscription.CaptureSequence = registration.Proxy.CaptureSequence;
          registration.Subscriptions.Add(subscriptionId, subscription);
        }
        RefreshPropertyFilter(registration);
      }
      catch
      {
        bool release;
        lock (_gate)
        {
          registration.Subscriptions.Remove(subscriptionId);
          subscription.MarkDisposed();
          release = registration.Subscriptions.Count == 0;
          if (release && IsRegistered(registration))
            _registrations.TryRemove(key, out _);
        }
        if (release)
        {
          try
          {
            DisposeRegistration(registration);
          }
          catch (Exception error)
          {
            Log.Debug(error, "Failed to release inactive property subscription for token={Token}", token);
          }
        }
        throw;
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
    TokenRegistration registration,
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
      if (!IsRegistered(registration))
        return;

      foreach (var subscription in registration.Subscriptions.Values)
      {
        if (context?.CaptureError is not null || subscription.Matches(propertyName ?? ""))
          matchingSubscriptions.Add(subscription);
      }
    }

    if (matchingSubscriptions.Count == 0 && PropertyChanged is null)
      return;

    var eventArgs = capturedArgs ?? new RemotePropertyChangedEventArgs(registration.Key.Token, propertyName ?? "", source);
    var groupId = $"token-pc-router:{registration.Key.Token}";

    SyncThread.Enqueue(() =>
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        return;

      PropertyChanged?.Invoke(this, eventArgs);

      foreach (var subscription in matchingSubscriptions)
        subscription.Raise(eventArgs);
    }, groupId);
  }

  private bool IsRegistered(TokenRegistration registration) =>
    _registrations.TryGetValue(registration.Key, out var current) && ReferenceEquals(current, registration);

  private void ThrowIfDetached(TokenRegistration registration)
  {
    if (Volatile.Read(ref _isDisposed) != 0 || !IsRegistered(registration))
      throw new ObjectDisposedException(nameof(PropertyChangeRouter));
  }

  // The caller holds UpdateLock so filter requests for this registration stay ordered.
  private void RefreshPropertyFilter(TokenRegistration registration)
  {
    string[] properties;
    lock (_gate)
    {
      properties = registration.Subscriptions.Values
        .SelectMany(subscription => subscription.TrackedProperties)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
    registration.Proxy!.SetTrackedProperties(properties);
  }

  private void RemoveSubscription(TokenRegistration registration, Guid id)
  {
    lock (registration.UpdateLock)
    {
      bool release;
      lock (_gate)
      {
        if (!IsRegistered(registration) || !registration.Subscriptions.TryGetValue(id, out var subscription))
          return;

        registration.Subscriptions.Remove(id);
        subscription.MarkDisposed();
        release = registration.Subscriptions.Count == 0;
        if (release)
          _registrations.TryRemove(registration.Key, out _);
      }

      if (release)
      {
        DisposeRegistration(registration);
      }
      else
      {
        // A failed narrowing request can leave extra native notifications enabled.
        // Local filters already reflect the removal and reject those notifications.
        try
        {
          RefreshPropertyFilter(registration);
        }
        catch (Exception error)
        {
          Log.Debug(error, "Failed to narrow property filter for token={Token}", registration.Key.Token);
        }
      }
    }
  }

  private void DisposeRegistration(TokenRegistration registration)
  {
    lock (registration.UpdateLock)
    {
      lock (_gate)
      {
        foreach (var subscription in registration.Subscriptions.Values)
          subscription.MarkDisposed();
        registration.Subscriptions.Clear();
      }

      if (registration.Proxy is not null)
      {
        registration.Proxy.PropertyChanged -= registration.Handler;
        registration.Proxy.Dispose();
      }
    }
  }
}