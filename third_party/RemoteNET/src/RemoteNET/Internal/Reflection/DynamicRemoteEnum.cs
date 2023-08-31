using System.Dynamic;


namespace RemoteNET.Internal.Reflection
{
  public class DynamicRemoteEnum : DynamicObject
  {
    private readonly RemoteEnum _remoteEnum;
    public RemoteApp App => _remoteEnum.App;

    public DynamicRemoteEnum(RemoteEnum remoteEnum)
    {
      _remoteEnum = remoteEnum;
    }

    public override bool TryGetMember(GetMemberBinder binder, out dynamic result)
    {
      string memberName = binder.Name;
      result = _remoteEnum.GetValue(memberName);
      return true;
    }

  }
}
