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
}
