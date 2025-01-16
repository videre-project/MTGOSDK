/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop;


namespace MTGOSDK.Core.Remoting.Hooking;

/// <summary>
/// Used by RemoteHarmony and Communicator
/// </summary>
public delegate void LocalHookCallback(
  HookContext context,
  ObjectOrRemoteAddress instance,
  ObjectOrRemoteAddress[] args
);
