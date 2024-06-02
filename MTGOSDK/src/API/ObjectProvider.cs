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

  /// <summary>
  /// Cache of all instances retrieved from the client's ObjectProvider.
  /// </summary>
  private static readonly ConcurrentDictionary<string, dynamic> s_instances = new();

  /// <summary>
  /// Store of all instances' setters for retrieved LazyRemoteObjects.
  /// </summary>
  private static readonly ConcurrentDictionary<string, TResetter> s_resetters = new();

  /// <summary>
  /// Whether the ObjectProvider cache requires a reset.
  /// </summary>
  public static bool RequiresReset = false;

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

      if (RequiresReset)
        Log.Warning("Retrieved type is stale and requires a refresh from ObjectProvider.");

      return instance;
    }

    //
    // Create a LazyRemoteObject instance and store its resetter for future use.
    //
    // If the instance is never referenced, then the ObjectProvider will never
    // query the client for the instance type and create no overhead.
    //
    if (useLazy || RequiresReset)
    {
      Log.Trace("Creating lazy instance of type {Type}", queryPath);
      instance = new LazyRemoteObject();
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
    // If the client is disposed, return an empty instance to defer construction
    else if (!RemoteClient.IsInitialized && !s_resetters.ContainsKey(queryPath))
    {
      Log.Trace("Client is disposed. Returning empty instance of type {Type}", queryPath);
      return Get(queryPath, useCache, useHeap, useLazy: true);
    }

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
    RemoteClient.Disposed += (s, e) =>
    {
      s_instances.TryRemove(queryPath, out _);
      Reset(queryPath, new Func<dynamic>(() => Get(queryPath, useCache, useHeap, useLazy)));
    };

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

  private static void Reset(string key, Func<dynamic> callback)
  {
    // Clear RemoteClient callback (if another instance was created)
    RemoteClient.Disposed -= (s, e) => Reset(key, callback);

    if (s_resetters.TryGetValue(key, out var s_reset))
    {
      Log.Trace("Resetting instance type {Type}", key);
      RequiresReset = true;
      s_reset(callback);
      RequiresReset = false;
    }
  }

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
    foreach (var kvp in s_instances)
      Reset(kvp.Key, new Func<dynamic>(() => Get(kvp.Key, useLazy: false)));
  }
}
