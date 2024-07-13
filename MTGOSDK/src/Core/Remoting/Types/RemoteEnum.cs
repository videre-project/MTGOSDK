/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Reflection;


namespace MTGOSDK.Core.Remoting.Types;

public class RemoteEnum(RemoteType remoteType)
{
  public RemoteHandle App => remoteType?.App;

  public object GetValue(string valueName)
  {
    RemoteFieldInfo verboseField = remoteType.GetField(valueName) as RemoteFieldInfo;
    return verboseField.GetValue(null);
  }

  public dynamic Dynamify() => new DynamicRemoteEnum(this);
}
