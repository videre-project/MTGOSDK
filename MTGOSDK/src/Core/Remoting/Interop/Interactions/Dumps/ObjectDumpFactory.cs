/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

/// <summary>
/// Factory for creating ObjectDump instances from object instances.
/// </summary>
public static class ObjectDumpFactory
{
  /// <summary>
  /// Cached type metadata to avoid repeated reflection calls.
  /// </summary>
  private static readonly ConcurrentDictionary<Type, CachedTypeMetadata> s_typeCache = new();

  /// <summary>
  /// Cached metadata for a type.
  /// </summary>
  private class CachedTypeMetadata
  {
    public List<MemberDump> Fields { get; set; }
    public List<MemberDump> Properties { get; set; }
  }

  /// <summary>
  /// Gets or creates cached metadata for a type.
  /// </summary>
  private static CachedTypeMetadata GetCachedMetadata(Type type)
  {
    return s_typeCache.GetOrAdd(type, t =>
    {
      var sw = Stopwatch.StartNew();

      // Get events for filtering
      var eventInfos = t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
      var eventNames = new HashSet<string>(eventInfos.Length);
      foreach (var eventInfo in eventInfos)
      {
        eventNames.Add(eventInfo.Name);
      }
      Log.Debug($"[ObjectDumpFactory:Cache] GetEvents: {sw.ElapsedMilliseconds}ms for {t.Name}");

      sw.Restart();
      var allFields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
      var fields = new List<MemberDump>(allFields.Length);
      foreach (var fieldInfo in allFields)
      {
        if (!eventNames.Contains(fieldInfo.Name))
          fields.Add(new MemberDump { Name = fieldInfo.Name });
      }
      Log.Debug($"[ObjectDumpFactory:Cache] GetFields: {sw.ElapsedMilliseconds}ms, count={fields.Count} for {t.Name}");

      sw.Restart();
      var allProps = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
      var props = new List<MemberDump>(allProps.Length);
      foreach (var propInfo in allProps)
      {
        if (propInfo.GetMethod != null)
          props.Add(new MemberDump { Name = propInfo.Name });
      }
      Log.Debug($"[ObjectDumpFactory:Cache] GetProperties: {sw.ElapsedMilliseconds}ms, count={props.Count} for {t.Name}");

      return new CachedTypeMetadata { Fields = fields, Properties = props };
    });
  }

  /// <summary>
  /// Creates an ObjectDump from an object instance.
  /// </summary>
  /// <param name="instance">The object instance to dump</param>
  /// <param name="retrievalAddr">The address where the object was retrieved from</param>
  /// <param name="pinAddr">The address where the object is pinned</param>
  /// <returns>The ObjectDump instance</returns>
  public static ObjectDump Create(
    object instance,
    ulong retrievalAddr,
    ulong pinAddr)
  {
    var sw = Stopwatch.StartNew();
    Type dumpedObjType = instance.GetType();
    Log.Debug($"[ObjectDumpFactory] GetType: {sw.ElapsedMilliseconds}ms, type={dumpedObjType.Name}");

    ObjectDump od;
    if (dumpedObjType.IsPrimitiveEtc())
    {
      od = new ObjectDump()
      {
        Type = instance.GetType().ToString(),
        RetrievalAddress = retrievalAddr,
        PinnedAddress = pinAddr,
        PrimitiveValue = PrimitivesEncoder.Encode(instance),
        HashCode = instance.GetHashCode()
      };
      return od;
    }
    else if (instance is Array enumerable)
    {
      Type elementsType = instance.GetType().GetElementType();

      if (elementsType.IsPrimitiveEtc())
      {
        // Collection of primitives can be encoded using the PrimitivesEncoder
        od = new ObjectDump()
        {
          ObjectType = ObjectType.Array,
          SubObjectsType = ObjectType.Primitive,
          RetrievalAddress = retrievalAddr,
          PinnedAddress = pinAddr,
          PrimitiveValue = PrimitivesEncoder.Encode(instance),
          SubObjectsCount = enumerable.Length,
          Type = dumpedObjType.FullName,
          HashCode = instance.GetHashCode()
        };
        return od;
      }
      else
      {
        // It's an array of objects. We need to treat it in a unique way.
        od = new ObjectDump()
        {
          ObjectType = ObjectType.Array,
          SubObjectsType = ObjectType.NonPrimitive,
          RetrievalAddress = retrievalAddr,
          PinnedAddress = pinAddr,
          PrimitiveValue = "==UNUSED==",
          SubObjectsCount = enumerable.Length,
          Type = dumpedObjType.FullName,
          HashCode = instance.GetHashCode()
        };

        dumpedObjType = typeof(Array);
        // Falling out of the `if` to go fill with all fields and such...
      }
    }
    else
    {
      // General non-array or primitive object
      sw.Restart();
      var hashCode = instance.GetHashCode();
      Log.Debug($"[ObjectDumpFactory] GetHashCode: {sw.ElapsedMilliseconds}ms");

      od = new ObjectDump()
      {
        ObjectType = ObjectType.NonPrimitive,
        RetrievalAddress = retrievalAddr,
        PinnedAddress = pinAddr,
        Type = dumpedObjType.FullName,
        HashCode = hashCode
      };
    }

    // Use cached metadata instead of repeated reflection
    sw.Restart();
    var metadata = GetCachedMetadata(dumpedObjType);
    Log.Debug($"[ObjectDumpFactory] GetCachedMetadata: {sw.ElapsedMilliseconds}ms (cache hit if 0ms)");

    // Populate fields and properties from cache
    od.Fields = metadata.Fields;
    od.Properties = metadata.Properties;

    return od;
  }
}

