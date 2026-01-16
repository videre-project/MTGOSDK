/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  /// <summary>
  /// Handles /batch_collection requests - fetches properties for all items in a collection.
  /// This avoids per-item pinning overhead by iterating the collection on the Diver side.
  /// </summary>
  private byte[] MakeBatchCollectionResponse()
  {
    Log.Debug("[Diver] Got /batch_collection request!");

    var request = DeserializeRequest<BatchCollectionRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    // Get the collection object
    if (!_runtime.TryGetPinnedObject(request.CollectionAddress, out object collection))
      return QuickError("Can't iterate an unpinned collection");

    // Verify it's enumerable
    if (collection is not IEnumerable enumerable)
      return QuickError($"Object at {request.CollectionAddress} is not IEnumerable");

    var paths = request.PathsDelimited?.Split('|') ?? Array.Empty<string>();
    var items = new List<Dictionary<string, string>>();
    var itemTokens = new List<ulong>();
    var types = new Dictionary<string, string>();
    int count = 0;
    int maxItems = request.MaxItems > 0 ? request.MaxItems : int.MaxValue;
    string itemTypeName = null;

    Log.Debug($"[Diver] Iterating collection with {paths.Length} paths, max={maxItems}");

    foreach (var item in enumerable)
    {
      if (count >= maxItems) break;

      // Pin the item and record its token for SDK to create DRO references
      ulong itemToken = _runtime.PinObject(item);
      itemTokens.Add(itemToken);

      // Capture item type name (first item defines it)
      if (itemTypeName == null && item != null)
      {
        itemTypeName = item.GetType().FullName;
      }

      var itemValues = new Dictionary<string, string>();

      foreach (var path in paths)
      {
        if (string.IsNullOrEmpty(path)) continue;

        try
        {
          var (value, type) = ResolveMemberPath(item, path);

          if (value == null)
          {
            itemValues[path] = null;
          }
          else if (value.GetType().IsEnum)
          {
            // Serialize enums as their string representation
            var stringValue = value.ToString();
            itemValues[path] = PrimitivesEncoder.Encode(stringValue);
            if (!types.ContainsKey(path))
              types[path] = "System.String";
          }
          else if (value.GetType().IsPrimitiveEtc())
          {
            itemValues[path] = PrimitivesEncoder.Encode(value);
            if (!types.ContainsKey(path))
              types[path] = value.GetType().FullName;
          }
          else if (IsSimpleCollection(value))
          {
            // Serialize simple collections as JSON
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            itemValues[path] = json;
            if (!types.ContainsKey(path))
              types[path] = value.GetType().FullName;
          }
          else
          {
            // Non-primitive/non-collection: skip or return null
            // We don't pin individual items to avoid overhead
            itemValues[path] = null;
          }
        }
        catch (Exception ex)
        {
          Log.Debug($"[Diver] Error resolving path '{path}' on item {count}: {ex.Message}");
          itemValues[path] = null;
        }
      }

      items.Add(itemValues);
      count++;
    }

    Log.Debug($"[Diver] Processed {count} items from collection, pinned {itemTokens.Count} items");

    var response = new BatchCollectionResponse
    {
      Items = items,
      Types = types,
      Count = count,
      ItemTokens = itemTokens,
      ItemTypeName = itemTypeName
    };

    return WrapSuccess(response);
  }

  /// <summary>
  /// Checks if a value is a simple collection that can be JSON-serialized.
  /// </summary>
  private static bool IsSimpleCollection(object value)
  {
    if (value == null) return false;
    var type = value.GetType();
    
    // Check for arrays of primitives
    if (type.IsArray && type.GetElementType()?.IsPrimitiveEtc() == true)
      return true;

    // Check for generic collections of primitives
    if (type.IsGenericType)
    {
      var genericArgs = type.GetGenericArguments();
      if (genericArgs.Length == 1 && genericArgs[0].IsPrimitiveEtc())
      {
        // Check if it implements IEnumerable
        if (typeof(IEnumerable).IsAssignableFrom(type))
          return true;
      }
    }

    return false;
  }
}
