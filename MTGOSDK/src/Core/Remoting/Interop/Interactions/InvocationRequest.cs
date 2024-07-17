/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

public class InvocationRequest
{
  public ulong ObjAddress { get; set; }
  public string MethodName { get; set; }
  public string TypeFullName { get; set; }
  public string[] GenericArgsTypeFullNames { get; set; } = [];
  public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();
}
