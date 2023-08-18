using System;
using System.Collections.Generic;
using System.Linq;

using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;

using RemoteNET.Internal.Reflection;


namespace RemoteNET.Internal
{
  public class DynamicRemoteObjectFactory
  {
    private RemoteApp _app;

    public DynamicRemoteObject Create(RemoteApp rApp, RemoteObject remoteObj, TypeDump typeDump)
    {
      _app = rApp;
      return new DynamicRemoteObject(rApp, remoteObj);
    }
  }
}
