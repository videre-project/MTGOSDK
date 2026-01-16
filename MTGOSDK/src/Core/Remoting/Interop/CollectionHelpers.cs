/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Reflection;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Comparison operators for remote filtering operations.
/// </summary>
public enum ComparisonOperator
{
  Equal = 0,
  NotEqual = 1,
  GreaterThan = 2,
  GreaterThanOrEqual = 3,
  LessThan = 4,
  LessThanOrEqual = 5
}

/// <summary>
/// Helper methods for performing LINQ-like operations on remote collections.
/// These methods execute filtering/sorting on the remote side to avoid 
/// per-item IPC calls for property access.
/// </summary>
/// <remarks>
/// This class is included in the assembly injected into the remote process,
/// allowing the SDK to invoke these methods via RemoteClient.InvokeMethod.
/// Element types are inferred at runtime to avoid marshaling SDK wrapper types.
/// </remarks>
public static class CollectionHelpers
{
  /// <summary>
  /// Filters a collection by comparing a property value against a given value.
  /// </summary>
  /// <param name="collection">The collection to filter.</param>
  /// <param name="propertyName">The name of the property to compare.</param>
  /// <param name="operatorCode">The comparison operator (see ComparisonOperator enum).</param>
  /// <param name="value">The value to compare against.</param>
  /// <returns>A list of items matching the predicate.</returns>
  public static List<object> WherePropertyCompare(
    object collection, 
    string propertyName, 
    int operatorCode, 
    object value)
  {
    var comparer = Comparer<object>.Default;
    var result = new List<object>();
    var propertyCache = new Dictionary<Type, PropertyInfo>();

    foreach (var item in (IEnumerable)collection)
    {
      if (item is null) continue;

      var type = item.GetType();
      if (!propertyCache.TryGetValue(type, out var property))
      {
         property = type.GetProperty(propertyName)
          ?? throw new ArgumentException($"Property '{propertyName}' not found on type '{type.Name}'");
         propertyCache[type] = property;
      }
      
      var propValue = property.GetValue(item);
      int cmp = comparer.Compare(propValue, value);
      
      bool matches = (ComparisonOperator)operatorCode switch
      {
        ComparisonOperator.Equal => cmp == 0,
        ComparisonOperator.NotEqual => cmp != 0,
        ComparisonOperator.GreaterThan => cmp > 0,
        ComparisonOperator.GreaterThanOrEqual => cmp >= 0,
        ComparisonOperator.LessThan => cmp < 0,
        ComparisonOperator.LessThanOrEqual => cmp <= 0,
        _ => throw new ArgumentException($"Unknown operator: {operatorCode}")
      };
      
      if (matches) result.Add(item);
    }
    
    return result;
  }

  /// <summary>
  /// Orders a collection by a property value.
  /// </summary>
  /// <param name="collection">The collection to sort.</param>
  /// <param name="propertyName">The name of the property to sort by.</param>
  /// <param name="descending">Whether to sort in descending order.</param>
  /// <returns>A sorted list.</returns>
  public static List<object> OrderByProperty(
    object collection, 
    string propertyName, 
    bool descending = false)
  {
    var items = ((IEnumerable)collection).Cast<object>().ToList();
    
    if (items.Count == 0)
      return items;
    
    var comparer = Comparer<object>.Default;
    var propertyCache = new Dictionary<Type, PropertyInfo>();
    
    // Helper to get property value from an item's actual runtime type
    object GetPropertyValue(object item)
    {
      if (item is null) return 0; // Treat nulls as default/min value? Or handle gracefully.
      
      var type = item.GetType();
      if (!propertyCache.TryGetValue(type, out var prop))
      {
        prop = type.GetProperty(propertyName)
          ?? throw new ArgumentException($"Property '{propertyName}' not found on type '{type.Name}'");
        propertyCache[type] = prop;
      }
      return prop.GetValue(item);
    }
    
    items.Sort((a, b) =>
    {
      var va = GetPropertyValue(a);
      var vb = GetPropertyValue(b);
      int result = comparer.Compare(va, vb);
      return descending ? -result : result;
    });
    
    return items;
  }

  /// <summary>
  /// Gets the element type from a collection type.
  /// </summary>
  private static Type GetElementType(Type collectionType)
  {
    // Try IEnumerable<T>
    var enumerable = collectionType.GetInterfaces()
      .Concat(new[] { collectionType }) // Include the type itself in case it's directly IEnumerable<T>
      .FirstOrDefault(i => 
        i.IsGenericType && 
        i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    
    if (enumerable != null)
      return enumerable.GetGenericArguments()[0];
    
    // Fallback to object
    return typeof(object);
  }
}
