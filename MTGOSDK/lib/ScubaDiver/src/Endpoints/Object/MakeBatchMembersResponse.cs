/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private static readonly BindingFlags AllMembers =
    BindingFlags.Public | BindingFlags.NonPublic |
    BindingFlags.Instance | BindingFlags.FlattenHierarchy;

  private byte[] MakeBatchMembersResponse()
  {
    Log.Debug("[Diver] Got /batch_members request!");

    var request = DeserializeRequest<BatchMembersRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    if (!_runtime.TryGetPinnedObject(request.ObjAddress, out object instance))
      return QuickError("Can't get members of an unpinned object");

    var paths = request.PathsDelimited?.Split('|') ?? Array.Empty<string>();

    // Filter empty paths and build ordered arrays
    var validPaths = paths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
    var schema = new string[validPaths.Length];
    var schemaTypes = new string[validPaths.Length];
    var values = new string[validPaths.Length];

    for (int i = 0; i < validPaths.Length; i++)
    {
      var path = validPaths[i];
      schema[i] = path;

      Log.Debug($"[Diver] Resolving path: {path}");
      try
      {
        var (value, type) = ResolveMemberPath(instance, path);

        if (value == null)
        {
          Log.Debug($"[Diver] Path '{path}' resolved to null");
          values[i] = null;
          schemaTypes[i] = "null";
        }
        else if (value.GetType().IsEnum)
        {
          var stringValue = value.ToString();
          values[i] = PrimitivesEncoder.Encode(stringValue);
          schemaTypes[i] = "System.String";
        }
        else if (value.GetType().IsPrimitiveEtc())
        {
          values[i] = PrimitivesEncoder.Encode(value);
          schemaTypes[i] = value.GetType().FullName ?? value.GetType().Name;
        }
        else if (value is System.Collections.IEnumerable enumerable && !(value is string))
        {
          var items = new List<object>();
          foreach (var item in enumerable)
            items.Add(item);

          values[i] = System.Text.Json.JsonSerializer.Serialize(items);
          schemaTypes[i] = value.GetType().FullName ?? value.GetType().Name;
        }
        else
        {
          ulong address = _runtime.PinObject(value);
          values[i] = $"@{address}";
          schemaTypes[i] = value.GetType().FullName ?? value.GetType().Name;
        }
      }
      catch (Exception ex)
      {
        Log.Debug($"[Diver] Failed to resolve path '{path}': {ex.Message}");
        values[i] = null;
        schemaTypes[i] = "error";
      }
    }

    var response = new BatchMembersResponse
    {
      Schema = schema,
      SchemaTypes = schemaTypes,
      Values = values
    };

    return WrapSuccess(response);
  }

  /// <summary>
  /// Resolves a dot-separated path to a value.
  /// E.g., "Rarity.Name" resolves obj.Rarity.Name
  /// Special case: "Property.ToString()" calls ToString() on the property value
  /// </summary>
  private (object Value, Type Type) ResolveMemberPath(object obj, string path)
  {
    if (obj == null || string.IsNullOrEmpty(path))
      return (null, null);

    // Check if path ends with .ToString() call
    bool callToString = path.EndsWith(".ToString()");
    if (callToString)
    {
      Log.Debug($"[Diver] Path '{path}' requests .ToString() conversion");
      // Strip .ToString() from the path for resolution
      path = path.Substring(0, path.Length - ".ToString()".Length);
      Log.Debug($"[Diver] Stripped to base path: '{path}'");
    }

    var current = obj;
    var segments = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

    foreach (var segment in segments)
    {
      if (current == null) return (null, null);

      var type = current.GetType();

      // Try property first (with full binding flags for non-public)
      var prop = type.GetProperty(segment, AllMembers);
      if (prop != null)
      {
        current = prop.GetValue(current);
        continue;
      }

      // Try field
      var field = type.GetField(segment, AllMembers);
      if (field != null)
      {
        current = field.GetValue(current);
        continue;
      }

      // Not found
      Log.Debug($"[Diver] Segment '{segment}' not found on type {type.Name}");
      return (null, null);
    }

    // If path specified .ToString(), call it on the final value
    if (callToString && current != null)
    {
      var stringValue = current.ToString();
      Log.Debug($"[Diver] Called .ToString() on {current.GetType().Name}, result: {stringValue}");
      current = stringValue;
    }

    return (current, current?.GetType());
  }
}
