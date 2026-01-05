/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

[MessagePackObject]
public class EventSubscriptionRequest
{
  [Key(0)]
  public ulong Address { get; set; }
  [Key(1)]
  public string EventName { get; set; }
}

[MessagePackObject]
public class EventUnsubscriptionRequest
{
  [Key(0)]
  public int Token { get; set; }
}

[MessagePackObject]
public class HookSubscriptionRequest
{
  [Key(0)]
  public string TypeFullName { get; set; }
  [Key(1)]
  public string MethodName { get; set; }
  [Key(2)]
  public string HookPosition { get; set; }
  [Key(3)]
  public List<string> ParametersTypeFullNames { get; set; }
}

[MessagePackObject]
public class HookUnsubscriptionRequest
{
  [Key(0)]
  public int Token { get; set; }
}
