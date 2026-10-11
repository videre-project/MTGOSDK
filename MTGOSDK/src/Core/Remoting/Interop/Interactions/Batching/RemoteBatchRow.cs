/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Contains the outputs or error for one source index, plus any nested collection rows.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchRow
{
  [Key(0)]
  public int CollectionOperationId { get; set; }
  [Key(1)]
  public int SourceIndex { get; set; }
  [Key(2)]
  public Dictionary<string, RemoteBatchValue> Values { get; set; } = new();
  [Key(3)]
  public RemoteBatchError? Error { get; set; }
  [Key(4)]
  public List<RemoteBatchRow> Rows { get; set; } = [];
}
