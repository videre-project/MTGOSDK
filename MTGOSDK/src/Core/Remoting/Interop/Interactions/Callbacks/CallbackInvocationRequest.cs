/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

[MessagePackObject]
public class CallbackInvocationRequest
{
  [Key(0)]
  public DateTime Timestamp { get; set; }
  [Key(1)]
  public int Token { get; set; }
  [Key(2)]
  public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();
}
