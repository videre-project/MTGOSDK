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

  /// <summary>
  /// Identifies one client (communicator) within its process, so several
  /// attachments from one process are separate registrations, and the Diver
  /// counts its pins and subscriptions to release them when it leaves. Null
  /// for an older client, which keeps the previous behaviour (its pins are
  /// shared and released only when the last client unregisters).
  /// </summary>
  [Key(1)]
  public string ClientId { get; set; }
}

[MessagePackObject]
public class UnregisterClientRequest
{
  [Key(0)]
  public int ProcessId { get; set; }

  /// <inheritdoc cref="RegisterClientRequest.ClientId"/>
  [Key(1)]
  public string ClientId { get; set; }
}
