/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// Associates synchronous hook and notification captures with a native owner.
/// Group names and owner paths are supplied by the caller, not interpreted by the Diver.
/// </summary>
[MessagePackObject]
public sealed class RemoteCaptureScope
{
  /// <summary>
  /// The capture group shared by participating registrations.
  /// </summary>
  [Key(0)] public string Group { get; set; } = "";

  /// <summary>
  /// The batch input containing the owner or the root of its member path.
  /// </summary>
  [Key(1)] public int OwnerInput { get; set; }

  /// <summary>
  /// An optional property/field path from that input to the native owner.
  /// </summary>
  [Key(2)] public string? OwnerPath { get; set; }

  /// <summary>
  /// Advances the owner's state revision when this invocation begins.
  /// Nested notification captures are deferred until this boundary completes.
  /// </summary>
  [Key(3)] public bool IsStateBoundary { get; set; }
}
