/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core;

public static class ObjectProvider
{
  /// <summary>
  /// Proxy type for the client's static ObjectProvider class.
  /// </summary>
  private static readonly Proxy<dynamic> s_proxy =
    new(typeof(WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider));

  public static dynamic Get(string queryPath)
  {
    // Get the RemoteType from the type's query path
    Type genericType = RemoteClient.GetInstanceType(queryPath);

    // Invoke the Get<T>() method on the client's ObjectProvider class
    return RemoteClient.InvokeMethod(s_proxy,
        methodName: "Get",
        genericTypes: new Type[] { genericType });
  }

  public static dynamic Get<T>(bool bindTypes = true)
  {
    // Create a proxy type for the given generic type
    Proxy<dynamic> proxy = new(typeof(T));

    //
    // If not binding types, return an instance leaving open all binding flags.
    //
    // However, as the proxy type creates a MemberInfo cache, any reflection on
    // the returned instance will check against the proxy type's cache when
    // determining access modifiers of the instance's members.
    //
    if(bindTypes == false)
      return RemoteClient.GetInstance(proxy);

    // Use the proxy type to retrieve the proxy value
    Type? @interface = proxy.Interface;
    dynamic obj = Get(@interface?.FullName ?? proxy);

    // Late bind the interface type to the proxy value
    if (bindTypes && @interface != null)
      obj = Proxy<dynamic>.As(obj, @interface);

    return obj;
  }
}
