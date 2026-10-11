/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Describes one ordered operation. Collection bodies and output projections
/// apply only to their corresponding operation kinds.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchOperation
{
  [Key(0)]
  public int Id { get; set; }

  [Key(1)]
  public RemoteBatchOperationKind Kind { get; set; }

  [Key(2)]
  public RemoteBatchReference? Target { get; set; }

  [Key(3)]
  public string? Path { get; set; }

  [Key(4)]
  public RemoteBatchMethod? Method { get; set; }

  [Key(5)]
  public RemoteBatchReference[] Arguments { get; set; } = [];

  [Key(6)]
  public List<RemoteBatchOperation> Body { get; set; } = [];

  [Key(7)]
  public int[]? Indices { get; set; }

  [Key(8)]
  public string? OutputName { get; set; }

  [Key(9)]
  public RemoteBatchProjection Projection { get; set; } = new();
}
