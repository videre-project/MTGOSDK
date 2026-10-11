/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Retains a dictionary key and value as separately tagged values.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchDictionaryEntry
{
  [Key(0)]
  public RemoteBatchValue Key { get; set; } = new();

  [Key(1)]
  public RemoteBatchValue Value { get; set; } = new();
}
