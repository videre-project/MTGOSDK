/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
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
  /// <summary>
  /// Handles /batch_collection requests - fetches properties for all items in a collection.
  /// Returns a columnar response where property names/types appear once and values are
  /// stored as Columns[propertyIndex][itemIndex].
  /// </summary>
  private byte[] MakeBatchCollectionResponse()
  {
    Log.Debug("[Diver] Got /batch_collection request!");

    var request = DeserializeRequest<BatchCollectionRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    if (!_runtime.TryGetPinnedObject(request.CollectionAddress, out object collection))
      return QuickError("Can't iterate an unpinned collection");

    if (collection is not IEnumerable enumerable)
      return QuickError($"Object at {request.CollectionAddress} is not IEnumerable");

    // Build schema from valid paths
    var schema = (request.PathsDelimited?.Split('|') ?? Array.Empty<string>())
      .Where(p => !string.IsNullOrEmpty(p))
      .ToArray();
    var schemaTypes = new string[schema.Length];
    var itemTokens = new List<ulong>();
    int maxItems = request.MaxItems > 0 ? request.MaxItems : int.MaxValue;
    string itemTypeName = null;

    Log.Debug($"[Diver] Iterating collection with {schema.Length} paths, max={maxItems}");

    // First pass: collect all items and their values into column lists
    var columnLists = new List<string?>[schema.Length];
    for (int c = 0; c < schema.Length; c++)
      columnLists[c] = new List<string?>();

    int count = 0;
    foreach (var item in enumerable)
    {
      if (count >= maxItems) break;

      ulong itemToken = _runtime.PinObject(item);
      itemTokens.Add(itemToken);

      if (itemTypeName == null && item != null)
        itemTypeName = item.GetType().FullName;

      for (int c = 0; c < schema.Length; c++)
      {
        try
        {
          var (value, type) = ResolveMemberPath(item, schema[c]);

          if (value == null)
          {
            columnLists[c].Add(null);
          }
          else if (value.GetType().IsEnum)
          {
            columnLists[c].Add(PrimitivesEncoder.Encode(value.ToString()));
            if (schemaTypes[c] == null) schemaTypes[c] = "System.String";
          }
          else if (value.GetType().IsPrimitiveEtc())
          {
            columnLists[c].Add(PrimitivesEncoder.Encode(value));
            if (schemaTypes[c] == null) schemaTypes[c] = value.GetType().FullName;
          }
          else if (IsSimpleCollection(value))
          {
            columnLists[c].Add(System.Text.Json.JsonSerializer.Serialize(value));
            if (schemaTypes[c] == null) schemaTypes[c] = value.GetType().FullName;
          }
          else
          {
            columnLists[c].Add(null);
          }
        }
        catch (Exception ex)
        {
          Log.Debug($"[Diver] Error resolving path '{schema[c]}' on item {count}: {ex.Message}");
          columnLists[c].Add(null);
        }
      }

      count++;
    }

    // Convert column lists to arrays
    var columns = new string?[schema.Length][];
    for (int c = 0; c < schema.Length; c++)
      columns[c] = columnLists[c].ToArray();

    // Fill in any null schema types with "null"
    for (int c = 0; c < schemaTypes.Length; c++)
      schemaTypes[c] ??= "null";

    Log.Debug($"[Diver] Processed {count} items from collection, pinned {itemTokens.Count} items");

    var response = new BatchCollectionResponse
    {
      Schema = schema,
      SchemaTypes = schemaTypes,
      Columns = columns,
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
