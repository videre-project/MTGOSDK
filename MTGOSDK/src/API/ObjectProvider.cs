/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Internal;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;


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
  /// Whether the ObjectProvider cache requires a refresh.
  /// </summary>
  internal static bool RequiresRefresh = false;

  /// <summary>
  /// Returns an instance of the given type from the client's ObjectProvider.
  /// </summary>
  /// <param name="queryPath">The query path of the registered type.</param>
  /// <param name="useCache">Whether to use the cached instance.</param>
  /// <param name="useHeap">Whether to query the client's object heap.</param>
  /// <returns>A remote instance of the given type.</returns>
  public static dynamic Get(
    string queryPath,
    bool useCache = true,
    bool useHeap = false)
  {
    // Check if the instance is already cached
    if (useCache && s_instances.TryGetValue(queryPath, out dynamic instance))
    {
      Log.Trace("Retrieved cached instance type {Type}", queryPath);
      return instance;
    }

    // If the client is disposed, return an empty instance to defer construction
    if (RemoteClient.IsDisposed)
    {
      Log.Trace("Client is disposed. Returning empty instance of type {Type}", queryPath);
      instance = new DynamicRemoteObject();
    }
    // Query using the ObjectProvider.Get<T>() method on the client
    else if (!useHeap)
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
  /// Refreshes all instances in the ObjectProvider cache.
  /// </summary>
  /// <remarks>
  /// This method is useful for updating all instances in the cache after
  /// connecting to a new MTGO instance or changing the client's context.
  /// </remarks>
  public static void Refresh()
  {
    // Ensure that a connection to the MTGO client has been established
    RemoteClient.EnsureInitialize();

    Log.Debug("Refreshing ObjectProvider cache.");
    foreach (var kvp in s_instances)
    {
      s_instances.TryGetValue(kvp.Key, out dynamic refObj);
      dynamic obj = Get(kvp.Key, useCache: false);
      Swap(ref refObj, obj, bindTypes: true);
    }
    RequiresRefresh = false;
  }
}
