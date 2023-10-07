/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core;

public enum ObjectScope
{
  Transient,
  Singleton
}

public static class ObjectProvider
{
  public static string FullName =>
    "WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider";

  public static dynamic Get(string queryPath)
  {
    // Get the RemoteType from the type's query path
    Type genericType = RemoteClient.GetInstanceType(queryPath);

    // Invoke the Get<T>() method on the client's ObjectProvider class
    return RemoteClient.InvokeMethod(FullName,
        methodName: "Get",
        genericTypes: new Type[] { genericType });
  }

  public static dynamic Get<T>(bool bindTypes = true)
  {
    // Use the proxy type to retrieve the interface type
    Proxy<dynamic> proxy = new(typeof(T));
    Type? @interface = proxy.Interface;

    // Retrieve the proxy value
    dynamic obj = Get(@interface?.FullName ?? proxy.ToString());

    // Late bind the interface type to the proxy value
    if (bindTypes && @interface != null)
      obj = Proxy<dynamic>.As(obj, @interface);

    return obj;
  }
}
