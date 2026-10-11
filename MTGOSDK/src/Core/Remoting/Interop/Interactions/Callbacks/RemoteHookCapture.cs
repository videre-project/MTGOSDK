/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;

using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// A batch executed synchronously at the configured hook position or scope completion.
/// </summary>
/// <remarks>
/// <para>
/// Each entry in Inputs selects a native parameter for the corresponding batch input.
/// Use -1 for the instance and nonnegative indices for method arguments. Unbound
/// inputs are decoded from the batch description.
/// </para>
/// <para>
/// Capture runs synchronously on the thread executing the native method, so the
/// batch's ForceUIThread setting must be false.
/// </para>
/// </remarks>
[MessagePackObject]
public sealed class RemoteHookCapture
{
  [Key(0)]
  public RemoteBatchRequest Batch { get; set; }
  [Key(1)]
  public List<int> Inputs { get; set; } = new();
  /// <summary>
  /// Whether callback delivery includes live instance/argument handles.
  /// Disable when the callback consumes only detached batch values.
  /// </summary>
  [Key(2)]
  public bool IncludeParameters { get; set; } = true;

  /// <summary>
  /// Encloses this synchronous invocation in a native capture scope.
  /// </summary>
  [Key(3)]
  public RemoteCaptureScope Scope { get; set; }

  /// <summary>
  /// Marks the scope changed even when no enrolled notification fires.
  /// </summary>
  [Key(4)]
  public bool CaptureOnExit { get; set; }

  /// <summary>
  /// Optional native filters evaluated in order. The first matching filter supplies
  /// the instance input. An empty list captures every invocation as before.
  /// </summary>
  [Key(5)]
  public List<RemoteHookFilter> Filters { get; set; } = new();
}
