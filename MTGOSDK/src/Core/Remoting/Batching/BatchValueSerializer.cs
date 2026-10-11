/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

using MessagePack;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// Serializes values using explicit projections or opted-in public scalar properties.
/// </summary>
/// <remarks>
/// Cycle detection tracks references along the current output path. This allows
/// an object to appear in separate branches while rejecting a reference back to
/// one of its ancestors.
/// </remarks>
internal sealed class BatchValueSerializer(
  RemoteBatchRequest request, Func<object?, string, object?> read,
  Func<RemoteBatchType, Type> resolveType, Func<object, ulong>? getObjectToken)
{
  private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_scalarMembers = new();

  private sealed class IdentityComparer : IEqualityComparer<object>
  {
    public static IdentityComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
  }

  public RemoteBatchValue Serialize(object? value, RemoteBatchProjection projection, string path) =>
    Serialize(value, projection, path, 0, new HashSet<object>(IdentityComparer.Instance));

  private RemoteBatchValue Serialize(object? value, RemoteBatchProjection projection,
    string path, int depth, HashSet<object> ancestors)
  {
    try
    {
      if (depth > request.MaxDepth) throw new InvalidOperationException("Projection depth limit exceeded.");
      if (value == null)
        return new() { Kind = RemoteBatchValueKind.Null };

      var type = value.GetType();
      projection = ResolveProjection(projection, type);
      if (projection.Kind == RemoteBatchProjectionKind.Value && type.IsEnum)
      {
        var underlying = Enum.GetUnderlyingType(type);
        return new()
        {
          Kind = RemoteBatchValueKind.Enum,
          Type = RemoteBatchType.Native(type),
          UnderlyingType = RemoteBatchType.Native(underlying),
          EnumName = type.IsDefined(typeof(FlagsAttribute), false) ? value.ToString() : Enum.GetName(type, value),
          ScalarData = MessagePackSerializer.Serialize(underlying, Convert.ChangeType(value, underlying))
        };
      }
      if (projection.Kind == RemoteBatchProjectionKind.Value && IsScalar(type))
        return new()
        {
          Kind = RemoteBatchValueKind.Scalar,
          Type = RemoteBatchType.Native(type),
          ScalarData = MessagePackSerializer.Serialize(type, value)
        };

      if (!type.IsValueType && !ancestors.Add(value))
        throw new InvalidOperationException("Object cycle in projection.");
      try
      {
        var result = new RemoteBatchValue { Type = RemoteBatchType.Native(type) };
        switch (projection.Kind)
        {
          case RemoteBatchProjectionKind.Value:
          case RemoteBatchProjectionKind.Sequence:
            if (value is not IEnumerable sequence || value is string || IsDictionary(type))
              throw new InvalidOperationException("Value requires an explicit object or dictionary projection.");
            result.Kind = RemoteBatchValueKind.Sequence;
            int index = 0;
            foreach (var item in sequence)
              result.Items.Add(Serialize(item, projection.Element ?? RemoteBatchProjection.Value(),
                $"{path}[{index++}]", depth + 1, ancestors));
            return result;

          case RemoteBatchProjectionKind.Object:
            result.Kind = RemoteBatchValueKind.Object;
            if (projection.IncludeLogicalAddress)
            {
              if (type.IsValueType)
                throw new InvalidOperationException("Logical addresses require reference objects.");
              result.LogicalAddress = (getObjectToken
                ?? throw new InvalidOperationException("The executor has no logical-address provider."))(value);
            }
            if (projection.ScalarContract != null || projection.RuntimeScalars)
            {
              var contract = projection.ScalarContract != null
                ? resolveType(projection.ScalarContract) : type;
              if (!contract.IsInstanceOfType(value))
                throw new ArgumentException($"Value does not implement {contract.FullName}.");
              foreach (var member in s_scalarMembers.GetOrAdd(contract, GetScalarMembers))
              {
                if (projection.Members.ContainsKey(member.Name)) continue;
                var memberPath = path + "." + member.Name;
                object? memberValue;
                try
                {
                  memberValue = member.GetValue(value, null);
                }
                catch (Exception error)
                {
                  throw new BatchProjectionException(memberPath, error);
                }
                result.Members.Add(member.Name, Serialize(memberValue, RemoteBatchProjection.Value(),
                  memberPath, depth + 1, ancestors));
              }
            }
            foreach (var member in projection.Members)
            {
              var memberPath = path + "." + member.Key;
              object? memberValue;
              try
              {
                memberValue = read(value, member.Value.Path);
              }
              catch (Exception error)
              {
                throw new BatchProjectionException(memberPath, error);
              }
              result.Members.Add(member.Key, Serialize(memberValue, member.Value.Projection,
                memberPath, depth + 1, ancestors));
            }
            return result;

          case RemoteBatchProjectionKind.Dictionary:
            if (!IsDictionary(type) || value is not IEnumerable entries)
              throw new InvalidOperationException("Dictionary projection requires a dictionary.");
            result.Kind = RemoteBatchValueKind.Dictionary;
            int entryIndex = 0;
            foreach (var entry in entries)
            {
              object? key, entryValue;
              if (entry is DictionaryEntry pair)
              {
                key = pair.Key;
                entryValue = pair.Value;
              }
              else
              {
                key = read(entry, "Key");
                entryValue = read(entry, "Value");
              }
              result.Entries.Add(new()
              {
                Key = Serialize(key, projection.DictionaryKey!, $"{path}[{entryIndex}].Key", depth + 1, ancestors),
                Value = Serialize(entryValue, projection.DictionaryValue!, $"{path}[{entryIndex}].Value", depth + 1, ancestors)
              });
              entryIndex++;
            }
            return result;
          default:
            throw new InvalidOperationException("Unsupported projection.");
        }
      }
      finally
      {
        if (!type.IsValueType) ancestors.Remove(value);
      }
    }
    catch (BatchProjectionException)
    {
      throw;
    }
    catch (Exception error)
    {
      throw new BatchProjectionException(path, error);
    }
  }

  private RemoteBatchProjection ResolveProjection(RemoteBatchProjection projection, Type type)
  {
    HashSet<RemoteBatchProjection>? visited = null;
    while (true)
    {
      RemoteBatchProjection selected;
      if (projection.Kind == RemoteBatchProjectionKind.Named)
        selected = request.Projections[projection.Name!];
      else if (!projection.RuntimeTypes.TryGetValue(type.FullName!, out selected!))
        return projection;

      // Selecting an alias or a runtime type can lead back to the same schema
      // without traversing an object member. Reject that loop before serialization.
      visited ??= new();
      if (!visited.Add(projection))
        throw new InvalidOperationException("Cyclic projection selection.");
      projection = selected;
    }
  }

  internal static bool IsScalar(Type type) =>
    (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr)) ||
    type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) ||
    type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(byte[]);

  private static PropertyInfo[] GetScalarMembers(Type contract)
  {
    var types = contract.IsInterface ? new[] { contract }.Concat(contract.GetInterfaces()) : new[] { contract };
    return types.SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
      .Where(member => member.GetIndexParameters().Length == 0 && member.GetGetMethod() != null)
      .Where(member =>
      {
        var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
        return type.IsEnum || IsScalar(type);
      })
      .GroupBy(member => member.Name, StringComparer.Ordinal)
      .Select(group => group.Select(member => member.PropertyType).Distinct().Count() == 1
        ? group.First() : throw new AmbiguousMatchException(group.Key))
      .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
  }

  private static bool IsDictionary(Type type) => typeof(IDictionary).IsAssignableFrom(type) ||
    type.GetInterfaces().Any(i => i.IsGenericType &&
      (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
       i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

  internal sealed class BatchProjectionException(string path, Exception inner)
    : Exception(inner.Message, inner)
  {
    public string Path { get; } = path;
  }
}
