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
    Type genericType = RemoteClient.GetInstanceType(queryPath);
    return RemoteClient.InvokeMethod(FullName,
        methodName: "Get",
        genericTypes: new Type[] { genericType });
  }

  public static dynamic Get<T>(Proxy<dynamic>? proxy=null)
  {
    proxy ??= new(null, typeof(T));
    return Get(proxy.Interface?.FullName ?? proxy.ToString());
  }
}
