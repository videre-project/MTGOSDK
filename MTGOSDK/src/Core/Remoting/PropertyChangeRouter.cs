/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.ComponentModel;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Remoting;

public interface IPropertyChangeSubscription : IDisposable
{
  ulong Token { get; }

  IReadOnlyCollection<string> TrackedProperties { get; }

  void AddTrackedProperties(params string[] propertyNames);

  bool RemoveTrackedProperties(params string[] propertyNames);

  void SetTrackedProperties(params string[] propertyNames);

  event EventHandler<RemotePropertyChangedEventArgs>? PropertyChanged;
}

/// <summary>
/// Routes token-tracked remote property change notifications to independent
/// subscriptions with additive local property filters.
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
    IEnumerable<string> initialProperties) : IPropertyChangeSubscription
  {
    private readonly PropertyChangeRouter _router = router;
    private readonly Guid _id = id;
    private readonly HashSet<string> _trackedProperties = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _isDisposed;

    public ulong Token { get; } = token;

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
      ThrowIfDisposed();
      if (propertyNames is null)
        throw new ArgumentNullException(nameof(propertyNames));

      lock (_gate)
      {
        foreach (var propertyName in propertyNames)
        {
          if (!string.IsNullOrWhiteSpace(propertyName))
            _trackedProperties.Add(propertyName);
        }
      }
    }

    public bool RemoveTrackedProperties(params string[] propertyNames)
    {
      ThrowIfDisposed();
      if (propertyNames is null)
        throw new ArgumentNullException(nameof(propertyNames));

      bool removed = false;
      lock (_gate)
      {
        foreach (var propertyName in propertyNames)
        {
          if (!string.IsNullOrWhiteSpace(propertyName))
            removed |= _trackedProperties.Remove(propertyName);
        }
      }

      return removed;
    }

    public void SetTrackedProperties(params string[] propertyNames)
    {
      ThrowIfDisposed();
      if (propertyNames is null)
        throw new ArgumentNullException(nameof(propertyNames));

      lock (_gate)
      {
        _trackedProperties.Clear();
        foreach (var propertyName in propertyNames)
        {
          if (!string.IsNullOrWhiteSpace(propertyName))
            _trackedProperties.Add(propertyName);
        }
      }
    }

    internal bool Matches(string propertyName)
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        return false;

      lock (_gate)
        return _trackedProperties.Contains(propertyName);
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

      _router.RemoveSubscription(Token, _id);

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

  private readonly ConcurrentDictionary<ulong, TokenRegistration> _registrations = new();
  private readonly object _gate = new();
  private int _isDisposed;

  public event EventHandler<RemotePropertyChangedEventArgs>? PropertyChanged;

  public int Count => _registrations.Count;

  public PropertyChangeRouter()
  {
    RemoteClient.Disposed += OnRemoteClientDisposed;
  }

  public IPropertyChangeSubscription Subscribe(
    DynamicRemoteObject dro,
    params string[] propertyNames)
  {
    if (Volatile.Read(ref _isDisposed) != 0)
      throw new ObjectDisposedException(nameof(PropertyChangeRouter));
    if (dro is null)
      throw new ArgumentNullException(nameof(dro));

    var remoteObject = dro.__ro
      ?? throw new ArgumentException(
        "DynamicRemoteObject has no backing remote object.",
        nameof(dro));

    ulong token = remoteObject.RemoteToken;
    var subscriptionId = Guid.NewGuid();
    var subscription = new PropertyChangeSubscription(
      this,
      token,
      subscriptionId,
      propertyNames ?? []);
    subscription.AddTrackedProperties(propertyNames ?? []);

    lock (_gate)
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        throw new ObjectDisposedException(nameof(PropertyChangeRouter));

      if (!_registrations.TryGetValue(token, out var registration))
      {
        var proxy = new RemotePropertyChangeProxy(remoteObject);

        PropertyChangedEventHandler handler = (_, args) =>
          OnProxyPropertyChanged(token, proxy, args);

        proxy.PropertyChanged += handler;

        registration = new TokenRegistration(
          proxy,
          handler,
          new Dictionary<Guid, PropertyChangeSubscription>());

        _registrations[token] = registration;
      }

      registration.Subscriptions[subscriptionId] = subscription;
    }

    return subscription;
  }

  public bool Unsubscribe(ulong token)
  {
    if (token == 0 || Volatile.Read(ref _isDisposed) != 0)
      return false;

    TokenRegistration? removed = null;
    lock (_gate)
    {
      if (_registrations.TryRemove(token, out var existing))
      {
        removed = existing;
      }
    }

    if (removed is null)
      return false;

    DisposeRegistration(removed);
    return true;
  }

  public void Clear()
  {
    KeyValuePair<ulong, TokenRegistration>[] entries;
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
    ulong token,
    INotifyPropertyChanged source,
    PropertyChangedEventArgs args)
  {
    if (Volatile.Read(ref _isDisposed) != 0)
      return;

    var propertyName = args?.PropertyName;
    if (string.IsNullOrWhiteSpace(propertyName))
      return;

    List<PropertyChangeSubscription> matchingSubscriptions = [];
    lock (_gate)
    {
      if (!_registrations.TryGetValue(token, out var registration))
        return;

      foreach (var subscription in registration.Subscriptions.Values)
      {
        if (subscription.Matches(propertyName))
          matchingSubscriptions.Add(subscription);
      }
    }

    if (matchingSubscriptions.Count == 0 && PropertyChanged is null)
      return;

    var eventArgs = new RemotePropertyChangedEventArgs(token, propertyName, source);
    var groupId = $"token-pc-router:{token}";

    SyncThread.Enqueue(() =>
    {
      if (Volatile.Read(ref _isDisposed) != 0)
        return;

      PropertyChanged?.Invoke(this, eventArgs);

      foreach (var subscription in matchingSubscriptions)
        subscription.Raise(eventArgs);
    }, groupId);
  }

  private void RemoveSubscription(ulong token, Guid id)
  {
    TokenRegistration? toDispose = null;

    lock (_gate)
    {
      if (!_registrations.TryGetValue(token, out var registration))
        return;

      if (!registration.Subscriptions.TryGetValue(id, out var subscription))
        return;

      registration.Subscriptions.Remove(id);
      subscription.MarkDisposed();

      if (registration.Subscriptions.Count == 0)
      {
        _registrations.TryRemove(token, out _);
        toDispose = registration;
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