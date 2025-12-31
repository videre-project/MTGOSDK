/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;


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
    // Get element type from collection
    var elementType = GetElementType(collection.GetType());
    var property = elementType.GetProperty(propertyName)
      ?? throw new ArgumentException($"Property '{propertyName}' not found on type '{elementType.Name}'");
    
    var comparer = Comparer<object>.Default;
    var result = new List<object>();

    foreach (var item in (IEnumerable)collection)
    {
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
    
    // Get element type from the first item's runtime type (not collection's declared type)
    // This is necessary because chained calls may pass List<object> containing typed items
    var elementType = items[0].GetType();
    var property = elementType.GetProperty(propertyName)
      ?? throw new ArgumentException($"Property '{propertyName}' not found on type '{elementType.Name}'");
    
    var comparer = Comparer<object>.Default;
    
    items.Sort((a, b) =>
    {
      var va = property.GetValue(a);
      var vb = property.GetValue(b);
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
