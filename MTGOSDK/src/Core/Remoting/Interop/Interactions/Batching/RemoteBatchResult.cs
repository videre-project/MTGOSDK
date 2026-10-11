/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Contains root outputs and collection rows returned by one batch execution.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchResult
{
  [Key(0)]
  public Dictionary<string, RemoteBatchValue> Values { get; set; } = new();
  [Key(1)]
  public List<RemoteBatchRow> Rows { get; set; } = [];
}
