/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Maps an output member to a native member path and its value projection.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchMemberProjection
{
  [Key(0)]
  public string Path { get; set; } = "";

  [Key(1)]
  public RemoteBatchProjection Projection { get; set; } = new();

  /// <summary>
  /// Omits this member if its read or projection fails. Other members are still
  /// captured, allowing the caller to provide a fallback for the omitted value.
  /// </summary>
  [Key(2)]
  public bool IgnoreErrors { get; set; }
}
