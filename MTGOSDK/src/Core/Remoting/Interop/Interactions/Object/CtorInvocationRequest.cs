/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Object;

[MessagePackObject]
public class CtorInvocationRequest
{
  [Key(0)]
  public string TypeFullName { get; set; }
  [Key(1)]
  public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();
  [Key(2)]
  public bool ForceUIThread { get; set; }
}

