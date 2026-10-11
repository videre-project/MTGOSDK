/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Represents a projected value whose kind determines which encoded contents to read.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchValue
{
  [Key(0)]
  public RemoteBatchValueKind Kind { get; set; }

  [Key(1)]
  public RemoteBatchType? Type { get; set; }

  [Key(2)]
  public byte[]? ScalarData { get; set; }

  [Key(3)]
  public string? EnumName { get; set; }

  [Key(4)]
  public List<RemoteBatchValue> Items { get; set; } = [];

  [Key(5)]
  public Dictionary<string, RemoteBatchValue> Members { get; set; } = new();

  [Key(6)]
  public List<RemoteBatchDictionaryEntry> Entries { get; set; } = [];

  [Key(7)]
  public RemoteBatchType? UnderlyingType { get; set; }

  [Key(8)]
  public ulong? LogicalAddress { get; set; }

  public T GetScalar<T>()
  {
    if (Kind != RemoteBatchValueKind.Scalar && Kind != RemoteBatchValueKind.Enum)
      throw new InvalidOperationException($"Value is {Kind}, not a scalar.");
    return MessagePackSerializer.Deserialize<T>(ScalarData!);
  }
}
