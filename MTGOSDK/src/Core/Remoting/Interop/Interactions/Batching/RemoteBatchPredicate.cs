/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;

using MTGOSDK.Core.Remoting.Interop;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Describes a collection predicate using member paths and batch values.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchPredicate
{
  [Key(0)]
  public RemoteBatchPredicateKind Kind { get; set; }

  [Key(1)]
  public string? Path { get; set; }

  [Key(2)]
  public string? OtherPath { get; set; }

  [Key(3)]
  public ComparisonOperator Operator { get; set; }

  [Key(4)]
  public RemoteBatchReference? Value { get; set; }

  [Key(5)]
  public RemoteBatchType? Type { get; set; }

  [Key(6)]
  public bool IgnoreCase { get; set; } = true;
}
