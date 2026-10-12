/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// Requests the retained batch of an existing event subscription without raising
/// a native event. Its values are delivered through the subscription's callback.
/// </summary>
[MessagePackObject]
public sealed class EventCaptureRequest
{
  [Key(0)]
  public int Token { get; set; }

  [Key(1)]
  public bool ForceUIThread { get; set; }
}
