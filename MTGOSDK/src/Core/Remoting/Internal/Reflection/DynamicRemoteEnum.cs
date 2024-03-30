/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Dynamic;


namespace MTGOSDK.Core.Remoting.Internal.Reflection;

public class DynamicRemoteEnum(RemoteEnum remoteEnum) : DynamicObject
{
  public RemoteApp App => remoteEnum.App;

  public override bool TryGetMember(GetMemberBinder binder, out dynamic result)
  {
    string memberName = binder.Name;
    result = remoteEnum.GetValue(memberName);
    return true;
  }
}
