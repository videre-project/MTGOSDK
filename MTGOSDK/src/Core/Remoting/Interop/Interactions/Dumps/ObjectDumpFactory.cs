/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MTGOSDK.Core.Remoting.Interop.Utils;


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
    Type dumpedObjType = instance.GetType();
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
      od = new ObjectDump()
      {
        ObjectType = ObjectType.NonPrimitive,
        RetrievalAddress = retrievalAddr,
        PinnedAddress = pinAddr,
        Type = dumpedObjType.FullName,
        HashCode = instance.GetHashCode()
      };
    }

    List<MemberDump> fields = new();
    var eventNames = dumpedObjType
      .GetEvents((BindingFlags)0xffff)
      .Select(eventInfo => eventInfo.Name);
    foreach (var fieldInfo in dumpedObjType
        .GetFields((BindingFlags)0xffff)
        .Where(fieldInfo => !eventNames.Contains(fieldInfo.Name)))
    {
      try
      {
        var fieldValue = fieldInfo.GetValue(instance);
        bool hasEncValue = false;
        string encValue = null;
        if (fieldValue != null)
        {
          hasEncValue = PrimitivesEncoder.TryEncode(fieldValue, out encValue);
        }

        fields.Add(new MemberDump()
        {
          Name = fieldInfo.Name,
          HasEncodedValue = hasEncValue,
          EncodedValue = encValue
        });
      }
      catch (Exception e)
      {
        fields.Add(new MemberDump()
        {
          Name = fieldInfo.Name,
          HasEncodedValue = false,
          RetrievalError = $"Failed to read. Exception: {e}"
        });
      }
    }

    List<MemberDump> props = new();
    foreach (var propInfo in dumpedObjType.GetProperties((BindingFlags)0xffff))
    {
      // Skip properties that don't have a getter
      if (propInfo.GetMethod == null)
        continue;

      try
      {
        //
        // Property dumping is disabled. It should be accessed using the 'get_' function.
        //

        //var propValue = propInfo.GetValue(instance);
        //bool hasEncValue = false;
        //string encValue = null;
        //if (propValue.GetType().IsPrimitiveEtc() || propValue is IEnumerable)
        //{
        //    hasEncValue = true;
        //    encValue = PrimitivesEncoder.Encode(propValue);
        //}

        props.Add(new MemberDump()
        {
          Name = propInfo.Name,
          HasEncodedValue = false,
          EncodedValue = null,
        });
      }
      catch (Exception e)
      {
        props.Add(new MemberDump()
        {
          Name = propInfo.Name,
          HasEncodedValue = false,
          RetrievalError = $"Failed to read. Exception: {e}"
        });
      }
    }

    // Populate fields and properties
    od.Fields = fields;
    od.Properties = props;

    return od;
  }
}
