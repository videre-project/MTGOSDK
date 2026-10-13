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

  /// <summary>
  /// An explicitly retained root value. Omitted for detached capture requests.
  /// </summary>
  [Key(5)]
  public RemoteBatchReference? ReferenceOutput { get; set; }

  /// <summary>
  /// Copies the wire description and preserves ownership of local input handles.
  /// </summary>
  internal RemoteBatchRequest Copy()
  {
    var copy = MessagePackSerializer.Deserialize<RemoteBatchRequest>(
      MessagePackSerializer.Serialize(this));
    for (int i = 0; i < Inputs.Count; i++)
      copy.Inputs[i].RetainedObject = Inputs[i].RetainedObject;
    return copy;
  }
}
