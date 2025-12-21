/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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

      sw.Restart();
      var fullName = dumpedObjType.FullName;
      Log.Debug($"[ObjectDumpFactory] FullName: {sw.ElapsedMilliseconds}ms, len={fullName?.Length}");

      od = new ObjectDump()
      {
        ObjectType = ObjectType.NonPrimitive,
        RetrievalAddress = retrievalAddr,
        PinnedAddress = pinAddr,
        Type = fullName,
        HashCode = hashCode
      };
    }

    sw.Restart();
    List<MemberDump> fields = new();
    var eventNames = dumpedObjType
      .GetEvents((BindingFlags) 0xffff)
      .Select(eventInfo => eventInfo.Name);
    Log.Debug($"[ObjectDumpFactory] GetEvents: {sw.ElapsedMilliseconds}ms");

    sw.Restart();
    var allFields = dumpedObjType.GetFields((BindingFlags) 0xffff);
    Log.Debug($"[ObjectDumpFactory] GetFields: {sw.ElapsedMilliseconds}ms, count={allFields.Length}");

    sw.Restart();
    foreach (var fieldInfo in allFields.Where(fieldInfo => !eventNames.Contains(fieldInfo.Name)))
    {
      // Only collect field names - values are fetched lazily via /get_field endpoint
      fields.Add(new MemberDump() { Name = fieldInfo.Name });
    }
    Log.Debug($"[ObjectDumpFactory] Field loop: {sw.ElapsedMilliseconds}ms, added={fields.Count}");

    sw.Restart();
    List<MemberDump> props = new();
    var allProps = dumpedObjType.GetProperties((BindingFlags) 0xffff);
    Log.Debug($"[ObjectDumpFactory] GetProperties: {sw.ElapsedMilliseconds}ms, count={allProps.Length}");

    sw.Restart();
    foreach (var propInfo in allProps)
    {
      // Skip properties that don't have a getter
      if (propInfo.GetMethod == null)
        continue;

      // Only collect property names - values are accessed via 'get_PropertyName' method
      props.Add(new MemberDump() { Name = propInfo.Name });
    }
    Log.Debug($"[ObjectDumpFactory] Property loop: {sw.ElapsedMilliseconds}ms, added={props.Count}");

    // Populate fields and properties
    od.Fields = fields;
    od.Properties = props;

    return od;
  }
}
