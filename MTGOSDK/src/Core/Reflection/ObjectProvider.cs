/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core;


namespace MTGOSDK.Core.Reflection;

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
  /// Returns an instance of the given type from the client's ObjectProvider.
  /// </summary>
  /// <param name="queryPath">The query path of the registered type.</param>
  /// <returns>A remote instance of the given type.</returns>
  public static dynamic Get(string queryPath)
  {
    // Get the RemoteType from the type's query path
    Type genericType = RemoteClient.GetInstanceType(queryPath);

    // Invoke the Get<T>() method on the client's ObjectProvider class
    return RemoteClient.InvokeMethod(s_proxy,
        methodName: "Get",
        genericTypes: new Type[] { genericType });
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
      if(bindTypes == false)
        return RemoteClient.GetInstance(proxy);
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
}
