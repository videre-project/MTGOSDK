/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

namespace MTGOSDK.Core.Remoting.Internal.Reflection;

public class RemoteEnum(RemoteType remoteType)
{
  public RemoteApp App => remoteType?.App;

  public object GetValue(string valueName)
  {
    // NOTE: This is breaking the "RemoteX"/"DynamicX" paradigm because we are
    // effectively returning a DRO here.
    //
    // Unlike RemoteObject which directly uses a remote token + TypeDump to
    // read/write fields/props/methods, RemoteEnum was created after
    // RemoteType was defined and it felt much easier to utilize it.
    //
    // RemoteType itself, as part of the reflection API, returns DROs.
    RemoteFieldInfo verboseField = remoteType.GetField(valueName) as RemoteFieldInfo;
    return verboseField.GetValue(null);
  }

  public dynamic Dynamify() => new DynamicRemoteEnum(this);
}
