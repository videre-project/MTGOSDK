/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.ObjectModel;
using System.Dynamic;

using MessagePack;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// A local, read-only view of explicitly projected object members.
/// </summary>
/// <remarks>
/// Construction copies the member map so later changes to the supplied map do not
/// affect this object. Values within the map, including arrays, keep their existing
/// references.
/// </remarks>
public sealed class RemoteBatchObject : DynamicObject, IEquatable<RemoteBatchObject>
{
  public IReadOnlyDictionary<string, object?> Members { get; }
  public RemoteBatchType? NativeType { get; }

  /// <summary>
  /// The native object's logical address, when explicitly projected.
  /// This identity does not retain the native object or permit remote access by itself.
  /// </summary>
  public ulong? LogicalAddress { get; }

  public RemoteBatchObject(IReadOnlyDictionary<string, object?> members,
    RemoteBatchType? nativeType, ulong? logicalAddress = null) : this(members)
  {
    NativeType = nativeType;
    LogicalAddress = logicalAddress;
  }

  public RemoteBatchObject(IReadOnlyDictionary<string, object?> members) =>
    Members = new ReadOnlyDictionary<string, object?>(
      members.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

  public override IEnumerable<string> GetDynamicMemberNames() => Members.Keys;

  public override bool TryGetMember(GetMemberBinder binder, out object? result)
  {
    if (Members.TryGetValue(binder.Name, out result)) return true;
    throw new MissingMemberException($"Member '{binder.Name}' was not captured.");
  }

  /// <summary>
  /// Reads a captured member, checking its local value type.
  /// </summary>
  /// <remarks>
  /// Native enums are converted by numeric value so values missing from the
  /// caller's enum declaration can still be read.
  /// </remarks>
  public T Get<T>(string name)
  {
    if (!Members.TryGetValue(name, out var value))
      throw new MissingMemberException($"Member '{name}' was not captured.");
    return ConvertValue<T>(value, name);
  }

  /// <summary>
  /// Reads the ordered elements of a captured sequence.
  /// </summary>
  public IReadOnlyList<T> GetSequence<T>(string name) => Array.AsReadOnly(
    Get<object?[]>(name).Select((value, index) => ConvertValue<T>(value, $"{name}[{index}]")).ToArray());

  /// <summary>
  /// Reads captured dictionary entries with their original key values.
  /// </summary>
  public IReadOnlyDictionary<TKey, TValue> GetDictionary<TKey, TValue>(string name)
    where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(
      Get<KeyValuePair<object?, object?>[]>(name).ToDictionary(
        pair => ConvertValue<TKey>(pair.Key, $"{name}.key"),
        pair => ConvertValue<TValue>(pair.Value, $"{name}.value")));

  private static T ConvertValue<T>(object? value, string path)
  {
    if (value is T typed) return typed;
    if (value == null && default(T) == null) return default!;
    if (typeof(T).IsEnum && value != null)
    {
      var number = value is RemoteBatchEnum captured ? captured.EnumValue : value;
      if (number is byte or sbyte or short or ushort or int or uint or long or ulong)
        return (T)Enum.ToObject(typeof(T), number);
    }
    throw new InvalidOperationException(
      $"Captured value '{path}' is {value?.GetType().FullName ?? "null"}, expected {typeof(T).FullName}.");
  }

  /// <summary>
  /// Creates a new captured object with the named member added or replaced.
  /// </summary>
  public RemoteBatchObject WithMember(string name, object? value)
  {
    var members = Members.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    members[name] = value;
    return new(members, NativeType, LogicalAddress);
  }

  public bool Equals(RemoteBatchObject? other) => other != null &&
    LogicalAddress == other.LogicalAddress && Members.Count == other.Members.Count
    && Members.All(pair =>
      other.Members.TryGetValue(pair.Key, out var value) &&
      CapturedValueComparer.Instance.Equals(pair.Value, value));

  public override bool Equals(object? obj) => obj is RemoteBatchObject other && Equals(other);

  public override int GetHashCode()
  {
    int hash = 0;
    foreach (var pair in Members)
      hash ^= HashCode.Combine(StringComparer.Ordinal.GetHashCode(pair.Key),
        CapturedValueComparer.Instance.GetHashCode(pair.Value));
    return HashCode.Combine(LogicalAddress, Members.Count, hash);
  }

  /// <summary>
  /// Materializes a projection without loading any native assemblies.
  /// </summary>
  public static object? FromValue(RemoteBatchValue value) => value.Kind switch
  {
    RemoteBatchValueKind.Null => null,
    RemoteBatchValueKind.Scalar => Scalar(value.Type!, value.ScalarData!),
    RemoteBatchValueKind.Enum => new RemoteBatchEnum(value.Type!,
      Scalar(value.UnderlyingType!, value.ScalarData!)!, value.EnumName),
    RemoteBatchValueKind.Object => new RemoteBatchObject(
      value.Members.ToDictionary(pair => pair.Key, pair => FromValue(pair.Value)), value.Type, value.LogicalAddress),
    RemoteBatchValueKind.Sequence => value.Items.Select(FromValue).ToArray(),
    RemoteBatchValueKind.Dictionary => value.Entries.Select(entry =>
      new KeyValuePair<object?, object?>(FromValue(entry.Key), FromValue(entry.Value))).ToArray(),
    _ => throw new InvalidOperationException($"Unsupported projected value {value.Kind}.")
  };

  private static object? Scalar(RemoteBatchType identity, byte[] data)
  {
    // Resolve scalar types from the local core library because the target CLR
    // may name mscorlib while the caller uses System.Private.CoreLib.
    var type = typeof(object).Assembly.GetType(identity.FullName)
      ?? throw new NotSupportedException($"Unsupported scalar type {identity.FullName}.");
    if (!BatchValueSerializer.IsScalar(type))
      throw new NotSupportedException($"Unsupported scalar type {identity.FullName}.");
    return MessagePackSerializer.Deserialize(type, data);
  }
}
