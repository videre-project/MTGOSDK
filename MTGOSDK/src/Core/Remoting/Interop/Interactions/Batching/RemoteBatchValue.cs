/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Represents a projected value whose kind determines which encoded contents to read.
/// </summary>
[MessagePackObject(AllowPrivate = true, SuppressSourceGeneration = true)]
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

  // Serialize the backing collections directly so unused value kinds do not
  // allocate them. Public collection access still returns a mutable empty value.
  [Key(4)]
  private List<RemoteBatchValue>? _items;

  [IgnoreMember]
  public List<RemoteBatchValue> Items
  {
    get => _items ??= [];
    set => _items = value;
  }

  [Key(5)]
  private Dictionary<string, RemoteBatchValue>? _members;

  [IgnoreMember]
  public Dictionary<string, RemoteBatchValue> Members
  {
    get => _members ??= new();
    set => _members = value;
  }

  [Key(6)]
  private List<RemoteBatchDictionaryEntry>? _entries;

  [IgnoreMember]
  public List<RemoteBatchDictionaryEntry> Entries
  {
    get => _entries ??= [];
    set => _entries = value;
  }

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
