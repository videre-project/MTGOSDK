/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using ScubaDiver.API.Interactions.Dumps;


namespace RemoteNET.Internal;

public class DynamicRemoteObjectFactory
{
  private RemoteApp _app;

  public DynamicRemoteObject Create(
    RemoteApp rApp,
    RemoteObject remoteObj,
    TypeDump typeDump)
  {
    _app = rApp;
    return new DynamicRemoteObject(rApp, remoteObj);
  }
}
