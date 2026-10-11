/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Describes which values to copy into a batch output. The Value projection
/// follows nulls, scalars, enums, and sequences. Object members and dictionary
/// entries must be described by their corresponding projections.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchProjection
{
  [Key(0)]
  public RemoteBatchProjectionKind Kind { get; set; }

  [Key(1)]
  public Dictionary<string, RemoteBatchMemberProjection> Members { get; set; } = new();

  [Key(2)]
  public RemoteBatchProjection? Element { get; set; }

  [Key(3)]
  public RemoteBatchProjection? DictionaryKey { get; set; }

  [Key(4)]
  public RemoteBatchProjection? DictionaryValue { get; set; }

  [Key(5)]
  public string? Name { get; set; }

  [Key(6)]
  public RemoteBatchType? ScalarContract { get; set; }

  [Key(7)]
  public bool RuntimeScalars { get; set; }

  [Key(8)]
  public Dictionary<string, RemoteBatchProjection> RuntimeTypes { get; set; } = new();

  [Key(9)]
  public bool IncludeLogicalAddress { get; set; }

  /// <summary>
  /// Includes the native object's logical address in the projected result.
  /// The Diver assigns or reuses this identity without extending the object's lifetime.
  /// </summary>
  public RemoteBatchProjection WithLogicalAddress()
  {
    IncludeLogicalAddress = true;
    return this;
  }

  /// <summary>
  /// Builds an object projection from the runtime type's public readable scalar
  /// properties. Add member projections for any complex values that are needed.
  /// </summary>
  public static RemoteBatchProjection ObjectScalars() =>
    new() { Kind = RemoteBatchProjectionKind.Object, RuntimeScalars = true };

  public static RemoteBatchProjection Value() => new();
  public static RemoteBatchProjection Named(string name) =>
    new() { Kind = RemoteBatchProjectionKind.Named, Name = name };
  public static RemoteBatchProjection Sequence(RemoteBatchProjection? element = null) =>
    new() { Kind = RemoteBatchProjectionKind.Sequence, Element = element ?? Value() };
  public static RemoteBatchProjection Dictionary(RemoteBatchProjection key,
    RemoteBatchProjection value) => new()
    {
      Kind = RemoteBatchProjectionKind.Dictionary,
      DictionaryKey = key,
      DictionaryValue = value
    };
  public static RemoteBatchProjection Object(params (string Name, string Path,
    RemoteBatchProjection Projection)[] members) => new()
    {
      Kind = RemoteBatchProjectionKind.Object,
      Members = members.ToDictionary(m => m.Name,
        m => new RemoteBatchMemberProjection { Path = m.Path, Projection = m.Projection })
    };

  /// <summary>
  /// Builds an object projection from a native contract's readable scalar properties.
  /// Additional member projections can supply complex values or replace a contract
  /// property with the same output name.
  /// </summary>
  public static RemoteBatchProjection ObjectScalars(RemoteBatchType contract,
    params (string Name, string Path, RemoteBatchProjection Projection)[] members)
  {
    var projection = Object(members);
    projection.ScalarContract = contract;
    return projection;
  }
}
