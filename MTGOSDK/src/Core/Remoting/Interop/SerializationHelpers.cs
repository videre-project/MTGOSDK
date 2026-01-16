/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Reflection;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Helper methods for batch property/field extraction on the Diver side.
/// Supports nested path resolution and batch operations on collections.
/// </summary>
/// <remarks>
/// This class is included in the assembly injected into the remote process,
/// allowing the SDK to invoke these methods via RemoteClient.InvokeMethod.
/// </remarks>
public static class SerializationHelpers
{
  private static readonly BindingFlags AllMembers =
    BindingFlags.Public | BindingFlags.NonPublic |
    BindingFlags.Instance | BindingFlags.FlattenHierarchy;

  /// <summary>
  /// Fetches values for multiple property paths from an object.
  /// Supports nested paths like "Rarity.Name".
  /// </summary>
  /// <param name="obj">The object to extract values from.</param>
  /// <param name="pathsDelimited">Pipe-delimited property path strings (e.g., "Name|Id|Rarity.Name").</param>
  /// <returns>Dictionary mapping path to resolved value.</returns>
  public static Dictionary<string, object?> GetMembersByPathDelimited(
    object obj,
    string pathsDelimited)
  {
    if (string.IsNullOrEmpty(pathsDelimited))
      return new Dictionary<string, object?>();
    
    var paths = pathsDelimited.Split('|');
    return GetMembersByPath(obj, paths);
  }

  /// <summary>
  /// Fetches values for multiple property paths from an object.
  /// Supports nested paths like "Rarity.Name".
  /// </summary>
  /// <param name="obj">The object to extract values from.</param>
  /// <param name="paths">Array of property path strings.</param>
  /// <returns>Dictionary mapping path to resolved value.</returns>
  public static Dictionary<string, object?> GetMembersByPath(
    object obj,
    string[] paths)
  {
    var result = new Dictionary<string, object?>();

    foreach (var path in paths)
    {
      result[path] = ResolvePath(obj, path);
    }

    return result;
  }

  /// <summary>
  /// Batch version: fetches values for multiple objects at once.
  /// </summary>
  /// <param name="objects">Array of objects to process.</param>
  /// <param name="paths">Array of property path strings.</param>
  /// <returns>List of dictionaries, one per object.</returns>
  public static List<Dictionary<string, object?>> GetMembersByPathBatch(
    object[] objects,
    string[] paths)
  {
    var results = new List<Dictionary<string, object?>>();

    foreach (var obj in objects)
    {
      results.Add(GetMembersByPath(obj, paths));
    }

    return results;
  }

  /// <summary>
  /// Batch version for collections (IEnumerable).
  /// </summary>
  public static List<Dictionary<string, object?>> GetMembersByPathFromCollection(
    object collection,
    string[] paths)
  {
    var results = new List<Dictionary<string, object?>>();

    foreach (var obj in (IEnumerable)collection)
    {
      results.Add(GetMembersByPath(obj, paths));
    }

    return results;
  }

  /// <summary>
  /// Resolves a dot-separated path to a value.
  /// E.g., "Rarity.Name" resolves obj.Rarity.Name
  /// </summary>
  private static object? ResolvePath(object? obj, string path)
  {
    if (obj == null || string.IsNullOrEmpty(path))
      return null;

    var current = obj;
    var segments = path.Split('.');

    foreach (var segment in segments)
    {
      if (current == null) return null;

      var type = current.GetType();

      // Try property first
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
      return null;
    }

    return current;
  }

  /// <summary>
  /// Checks if a value is a primitive type that can be serialized directly.
  /// </summary>
  public static bool IsPrimitive(object? value)
  {
    if (value == null) return true;

    var type = value.GetType();
    return type.IsPrimitive ||
           type == typeof(string) ||
           type == typeof(DateTime) ||
           type == typeof(TimeSpan) ||
           type == typeof(Guid) ||
           type == typeof(decimal) ||
           type.IsEnum;
  }

  /// <summary>
  /// Gets the return type of a property path for type checking.
  /// </summary>
  public static Type? GetPathReturnType(object obj, string path)
  {
    var value = ResolvePath(obj, path);
    return value?.GetType();
  }
}
