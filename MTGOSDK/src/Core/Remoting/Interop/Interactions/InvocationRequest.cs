/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions;

[MessagePackObject]
public class InvocationRequest
{
  [Key(0)]
  public ulong ObjAddress { get; set; }
  [Key(1)]
  public string MethodName { get; set; }
  [Key(2)]
  public string TypeFullName { get; set; }
  [Key(3)]
  public string[] GenericArgsTypeFullNames { get; set; } = Array.Empty<string>();
  [Key(4)]
  public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();
}
