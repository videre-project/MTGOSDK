/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// Replaces a native property filter without detaching its event handler.
/// </summary>
[MessagePackObject]
public sealed class EventPropertyFilterRequest
{
  [Key(0)]
  public int Token { get; set; }
  [Key(1)]
  public string[] PropertyNames { get; set; }
}
