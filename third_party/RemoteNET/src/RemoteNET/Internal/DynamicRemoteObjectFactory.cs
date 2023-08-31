using ScubaDiver.API.Interactions.Dumps;


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
