/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Client;

[MessagePackObject]
public class RegisterClientRequest
{
  [Key(0)]
  public int ProcessId { get; set; }
}

[MessagePackObject]
public class UnregisterClientRequest
{
  [Key(0)]
  public int ProcessId { get; set; }
}
