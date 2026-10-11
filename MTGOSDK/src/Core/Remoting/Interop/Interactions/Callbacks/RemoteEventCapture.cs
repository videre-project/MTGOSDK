/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;

using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// A batch captured on the native thread before queueing callback delivery.
/// </summary>
/// <remarks>
/// <para>
/// Inputs maps batch input indices to event parameters. Parameter 0 is the sender
/// and parameter 1 is the event arguments. Unbound inputs are decoded from the
/// batch description.
/// </para>
/// <para>
/// An enclosing capture scope defers projection until its invocation completes.
/// Without an enclosing scope, the batch runs synchronously inside the event handler.
/// Both paths run on the native thread, so ForceUIThread must be false.
/// </para>
/// </remarks>
[MessagePackObject]
public sealed class RemoteEventCapture
{
  [Key(0)]
  public RemoteBatchRequest Batch { get; set; }

  [Key(1)]
  public Dictionary<int, int> Inputs { get; set; } = new();

  /// <summary>
  /// Enrolls the notification in a native invocation scope so its projection can
  /// run at completion. Notifications outside that scope capture immediately.
  /// </summary>
  [Key(2)]
  public RemoteCaptureScope Scope { get; set; }
}
