/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;
using static MTGOSDK.Core.Remoting.LazyRemoteObject;


namespace MTGOSDK.API;

/// <summary>
/// Global manager for all singleton objects registered with the client.
/// </summary>
public static class ObjectProvider
{
  /// <summary>
  /// Proxy type for the client's static ObjectProvider class.
  /// </summary>
  private static readonly Proxy<dynamic> s_proxy =
    new(typeof(WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider));

  private static readonly ConcurrentDictionary<string, dynamic> s_instances = new();
  private static readonly ConcurrentDictionary<string, TResetter> s_resetters = new();
  private static readonly ConcurrentDictionary<string, Func<dynamic>> s_callbacks = new();

  /// <summary>
  /// Event raised when the ObjectProvider cache is reset.
  /// </summary>
  public static event EventHandler? OnReset;
  private static object s_lock = new();

  /// <summary>
  /// Registers a callback to reset the instance of the given type.
  /// </summary>
  /// <param name="key">The query path of the registered type.</param>
  /// <param name="callback">The callback to reset the instance.</param>
  private static void RegisterCallback(string key, Func<dynamic> callback)
  {
    if (!s_callbacks.TryAdd(key, callback))
      throw new InvalidOperationException("A duplicate callback was registered.");

    OnReset += (s, e) => Reset(key, callback);
    RemoteClient.Disposed += (s, e) => Reset(key, callback);
  }

  /// <summary>
  /// Clears the callback to reset the instance of the given type.
  /// </summary>
  /// <param name="key">The query path of the registered type.</param>
  /// <param name="callback">The callback used to reset the instance.</param>
  private static void ClearCallback(string key, Func<dynamic>? callback = null)
  {
    if (!s_callbacks.TryRemove(key, out var oldCallback))
      throw new InvalidOperationException("The callback was not registered.");

    callback ??= oldCallback;
    OnReset -= (s, e) => Reset(key, callback);
    RemoteClient.Disposed -= (s, e) => Reset(key, callback);
  }

  /// <summary>
  /// Resets the instance of the given type in the ObjectProvider cache.
  /// </summary>
  /// <param name="key">The query path of the registered type.</param>
  /// <param name="callback">The callback used to reset the instance.</param>
  private static void Reset(string key, Func<dynamic> callback)
  {
    lock (s_lock)
    {
      // Skip if no instance exists for the given key.
      if (!s_instances.TryRemove(key, out _) ||
          !s_resetters.TryGetValue(key, out var s_reset)) return;

      Log.Trace("Resetting instance type {Type}", key);
      s_reset(callback);
      ClearCallback(key, callback);
    }
  }

  /// <summary>
  /// Creates a lazy instance of the given type to later invoke ObjectProvider.
  /// </summary>
  /// <param name="queryPath">The query path of the registered type.</param>
  /// <param name="useCache">Whether to use the cached instance.</param>
  /// <param name="useHeap">Whether to query the client's object heap.</param>
  /// <returns>A lazy wrapper for a remote instance of the given type.</returns>
  /// <remarks>
  /// This method will explicitly disable creating another lazy instance in
  /// the created callback as the <see cref="Get"/> method will invoke this
  /// method when the <paramref name="useLazy"/> parameter is set to <c>true</c>.
  /// </remarks>
  private static dynamic Defer(
    string queryPath,
    bool useCache = true,
    bool useHeap = false)
  {
    Log.Trace("Creating lazy instance of type {Type}", queryPath);
    dynamic instance = new LazyRemoteObject();
    var resetter = instance.Set(new Func<dynamic>(() =>
        Get(queryPath, useCache, useHeap, useLazy: false)));

    // Store the resetter callback to reset the lazy instance when
    // the RemoteClient is disposed or the ObjectProvider cache is reset.
    s_resetters.AddOrUpdate(queryPath, resetter,
      // If a callback already exists, combine the two callbacks.
      new Func<string, TResetter, TResetter>((_, oldResetter) =>
        (TResetter)Delegate.Combine(oldResetter, resetter)
      ));

    // Return the lazy instance directly, as the later call to ObjectProvider
    // will add the created instance to the cache.
    return instance;
  }

  /// <summary>
  /// Returns an instance of the given type from the client's ObjectProvider.
  /// </summary>
  /// <param name="queryPath">The query path of the registered type.</param>
  /// <param name="useCache">Whether to use the cached instance.</param>
  /// <param name="useHeap">Whether to query the client's object heap.</param>
  /// <param name="useLazy">Whether to lazy-load the returned instance.</param>
  /// <returns>A remote instance of the given type.</returns>
  public static dynamic Get(
    string queryPath,
    bool useCache = true,
    bool useHeap = false,
    bool useLazy = true)
  {
    // Check if the instance is already cached
    if (useCache && s_instances.TryGetValue(queryPath, out dynamic instance))
    {
      Log.Trace("Retrieved cached instance type {Type}", queryPath);
      return instance;
    }
    // Otherwise create a lazy instance and store its resetter for future use.
    else if (useLazy) return Defer(queryPath, useCache, useHeap);

    // Query using the ObjectProvider.Get<T>() method on the client
    if (!useHeap)
    {
      // Get the RemoteType from the type's query path
      Log.Trace("Retrieving instance type {Type}", queryPath);
      Type genericType = RemoteClient.GetInstanceType(queryPath);

      // Invoke the Get<T>() method on the client's ObjectProvider class
      instance = RemoteClient.InvokeMethod(s_proxy, "Get", [genericType]);
    }
    // Query the for the instance type from the client's object heap
    else
    {
      Log.Trace("Retrieving heap instance of type {Type}", queryPath);
      instance = RemoteClient.GetInstance(queryPath);
    }

    // Cache the instance for future use
    s_instances.TryAdd(queryPath, instance);

    // Register a callback to reset the instance when disposed
    RegisterCallback(queryPath,
      new Func<dynamic>(() => Get(queryPath, useCache, useHeap, useLazy)));

    return instance;
  }

  /// <summary>
  /// Returns an instance of the given type from the client's ObjectProvider.
  /// </summary>
  /// <typeparam name="T">The class or interface type to retrieve.</typeparam>
  /// <param name="bindTypes">Whether to bind the type to the returned instance.</param>
  /// <returns>A remote instance of the given type.</returns>
  public static dynamic Get<T>(bool bindTypes = true) where T : class
  {
    // Create a proxy type for the given generic type
    Proxy<T> proxy = new();

    //
    // If not binding types, return an instance leaving open all binding flags.
    //
    // However, as the proxy type creates a MemberInfo cache, any reflection on
    // the returned instance will check against the proxy type's cache when
    // determining access modifiers of the instance's members.
    //
    try
    {
      if(bindTypes == false) return Get(proxy, useHeap: true);
    }
    catch { /* Input type was not instantiated/instantiable on the client. */ }

    // Use the proxy type to retrieve the proxy value
    Type? @interface = !proxy.IsInterface ? proxy.Interface : null;
    dynamic obj = Get(@interface?.FullName ?? proxy);

    // Late bind the interface type to the proxy value
    if (bindTypes && (@interface != null || proxy.IsInterface))
      obj = Proxy<dynamic>.As(obj, @interface ?? proxy.Class);

    return obj;
  }

  /// <summary>
  /// Returns an instance of the given type from the client's ObjectProvider.
  /// </summary>
  /// <typeparam name="T">The class or interface type to retrieve.</typeparam>
  /// <returns>A remote instance of the given type.</returns>
  public static T Get<T>() where T : class => Get<T>(bindTypes: true);

  /// <summary>
  /// Resets all instances in the ObjectProvider cache.
  /// </summary>
  /// <remarks>
  /// This method is useful for updating all instances in the cache after
  /// connecting to a new MTGO instance or changing the client's context.
  /// </remarks>
  public static void ResetCache()
  {
    Log.Debug("Resetting ObjectProvider cache.");
    OnReset?.Invoke(null, EventArgs.Empty);
  }
}
