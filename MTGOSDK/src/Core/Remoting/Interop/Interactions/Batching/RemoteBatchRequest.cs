/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;

using MTGOSDK.Core.Remoting.Interop;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Carries encoded inputs, ordered operations, and named projections for one execution.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchRequest
{
  [Key(0)]
  public List<ObjectOrRemoteAddress> Inputs { get; set; } = [];

  [Key(1)]
  public List<RemoteBatchOperation> Operations { get; set; } = [];

  [Key(2)]
  public Dictionary<string, RemoteBatchProjection> Projections { get; set; } = new();

  [Key(3)]
  public int MaxDepth { get; set; } = 32;

  [Key(4)]
  public bool ForceUIThread { get; set; }
}
