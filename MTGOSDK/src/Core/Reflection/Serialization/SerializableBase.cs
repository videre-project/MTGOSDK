/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;


namespace MTGOSDK.Core.Reflection.Serialization;

public abstract class SerializableBase : IJsonSerializable
{
  private static readonly ConcurrentDictionary<Type, PropertyFilter> k__PropertyFilters = new();
  // Cache for interface property names used by SerializeAs<T>()
  private static readonly ConcurrentDictionary<Type, IList<string>> s_interfacePropertyNames = new();

  // Fast path: Cache mapping (sourceType, interfaceType) -> list of (sourceProperty, interfaceProperty, needsConversion) tuples
  private static readonly ConcurrentDictionary<(Type, Type), IList<(PropertyInfo source, PropertyInfo target, bool needsConversion)>> s_propertyMappingCache = new();

  // Cache for compiled property getters: PropertyInfo -> Func<object, object>
  private static readonly ConcurrentDictionary<PropertyInfo, Func<object, object>> s_compiledGetters = new();

  private Type k__DerivedType = null!;
  private IList<PropertyInfo>? k__SerializableProperties;

  internal IList<PropertyInfo> SerializableProperties =>
    k__SerializableProperties ??=
      k__PropertyFilters
        .GetOrAdd(k__DerivedType ??= this.GetType(), _ => new(k__DerivedType))
        .Properties;

  /// <summary>
  /// Configures which properties to include or exclude from serialization.
  /// </summary>
  /// <param name="derivedType">The type of the object to serialize.</param>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">
  /// If true, only the properties in the include list will be serialized.
  /// </param>
  public static void SetSerializationProperties(
    Type derivedType,
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    // Create a new PropertyFilter with the specified properties.
    var filter = new PropertyFilter(include, exclude, strict, derivedType);
    k__PropertyFilters.AddOrUpdate(derivedType, filter, (_, _) => filter);
  }

  /// <summary>
  /// Configures which properties to include or exclude from serialization.
  /// </summary>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">
  /// If true, only the properties in the include list will be serialized.
  /// </param>
  public void SetSerializationProperties(
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    // Set the serialization properties for the current instance.
    k__DerivedType = this.GetType();
    var filter = new PropertyFilter(include, exclude, strict, k__DerivedType);
    k__SerializableProperties = filter.Properties;
  }

#if !MTGOSDKCORE
  /// <summary>
  /// Serializes the object to a specified interface type.
  /// </summary>
  /// <typeparam name="TInterface">
  /// The interface type to serialize to.
  /// </typeparam>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">
  /// If true, only the properties in the include list will be serialized.
  /// </param>
  /// <returns>
  /// An object of the specified interface type with the serialized properties.
  /// </returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the specified type is not an interface.
  /// </exception>
  /// <remarks>
  /// This method uses reflection to create a dynamic proxy of the specified
  /// interface type and populates it only properties specified by the interface
  /// and the include/exclude lists. This disassociates the object from the
  /// underlying type and prevents reflection on any hidden properties.
  /// </remarks>
  public TInterface SerializeAs<TInterface>(
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    var interfaceType = typeof(TInterface);
    if (!interfaceType.IsInterface)
    {
      throw new ArgumentException(
        $"The specified type {interfaceType} must be an interface.");
    }

    // Hydrate: batch-fetch all primitive properties for this interface
    // before parallel property access to minimize IPC calls
    if (this is DLRWrapper wrapper)
    {
      var paths = AccessPathAnalyzer.GetBatchablePathsForInterface(
        this.GetType(), interfaceType);
      if (paths.Length > 0)
      {
        wrapper.HydrateForInterface<TInterface>(paths);
      }
    }

    // Use the fast path: get or build property mappings
    var sourceType = this.GetType();
    var cacheKey = (sourceType, interfaceType);

    var propertyMappings = s_propertyMappingCache.GetOrAdd(cacheKey, key =>
    {
      var (srcType, ifaceType) = key;
      var mappings = new List<(PropertyInfo source, PropertyInfo target, bool needsConversion)>();

      // Get interface properties
      var ifaceProps = ifaceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

      // Get source properties
      var sourceProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .ToDictionary(p => p.Name, p => p);

      foreach (var ifaceProp in ifaceProps)
      {
        if (sourceProps.TryGetValue(ifaceProp.Name, out var sourceProp))
        {
          // Skip properties marked with [NonSerializable]
          if (sourceProp.GetCustomAttribute<NonSerializableAttribute>() != null)
            continue;
            
          // Pre-compile the getter for this property
          GetOrCompileGetter(sourceProp);
          // Determine if type conversion is needed (optimization: skip conversion call when types match)
          var needsConversion = sourceProp.PropertyType != ifaceProp.PropertyType && 
                               !ifaceProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType);
          mappings.Add((sourceProp, ifaceProp, needsConversion));
        }
      }

      return mappings;
    });

    // Apply include/exclude filters if needed
    IEnumerable<(PropertyInfo source, PropertyInfo target, bool needsConversion)> effectiveMappings = propertyMappings;
    if ((include != null && include.Count > 0) || (exclude != null && exclude.Count > 0))
    {
      effectiveMappings = propertyMappings
        .Where(m => (include == null || include.Count == 0 || include.Contains(m.source.Name)) &&
                    (exclude == null || !exclude.Contains(m.source.Name)));
    }

    // Fast path: use ConcurrentDictionary for thread-safe parallel property access
    var results = new ConcurrentDictionary<string, object>();

    // Convert mappings to list for parallel processing
    var mappingsList = effectiveMappings.ToList();
    
    // Parallelize property access to overlap IPC latency for remote objects
    // Bounded parallelism avoids thread pool exhaustion
    Parallel.ForEach(
      mappingsList,
      new ParallelOptions { MaxDegreeOfParallelism = Math.Min(mappingsList.Count, Environment.ProcessorCount) },
      mapping =>
      {
        var (source, target, needsConversion) = mapping;
        
        object value = s_compiledGetters.TryGetValue(source, out var getter)
          ? getter(this)
          : source.GetValue(this);
        
        results[target.Name] = needsConversion 
          ? ConvertValueToTargetType(value, target.PropertyType) 
          : value;
      });

    // Build ExpandoObject from results
    var expando = new ExpandoObject();
    var expandoDict = (IDictionary<string, object>)expando;
    foreach (var kvp in results)
    {
      expandoDict[kvp.Key] = kvp.Value;
    }

    return (TInterface)BindExpandoToInterface(expando, interfaceType);
  }

  /// <summary>
  /// Asynchronously serializes the object to a specified interface type.
  /// Uses Task.WhenAll to overlap IPC latency across properties.
  /// </summary>
  /// <typeparam name="TInterface">The interface type to serialize to.</typeparam>
  /// <param name="include">Properties to include.</param>
  /// <param name="exclude">Properties to exclude.</param>
  /// <param name="strict">If true, only the properties in the include list will be serialized.</param>
  /// <returns>A task that resolves to an object of the specified interface type.</returns>
  /// <remarks>
  /// This method is designed to be called from an outer parallelism context
  /// (e.g., SerializeAllAsync with Parallel.ForEachAsync). Property fetches
  /// are sequential within each item to avoid thread pool contention.
  /// </remarks>
  public Task<TInterface> SerializeAsAsync<TInterface>(
    IList<string> include = default,
    IList<string> exclude = default,
    bool strict = false)
  {
    var interfaceType = typeof(TInterface);
    if (!interfaceType.IsInterface)
    {
      throw new ArgumentException(
        $"The specified type {interfaceType} must be an interface.");
    }

    // Use the fast path: get or build property mappings (same as sync version)
    var sourceType = this.GetType();
    var cacheKey = (sourceType, interfaceType);

    var propertyMappings = s_propertyMappingCache.GetOrAdd(cacheKey, key =>
    {
      var (srcType, ifaceType) = key;
      var mappings = new List<(PropertyInfo source, PropertyInfo target, bool needsConversion)>();

      var ifaceProps = ifaceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
      var sourceProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .ToDictionary(p => p.Name, p => p);

      foreach (var ifaceProp in ifaceProps)
      {
        if (sourceProps.TryGetValue(ifaceProp.Name, out var sourceProp))
        {
          GetOrCompileGetter(sourceProp);
          var needsConversion = sourceProp.PropertyType != ifaceProp.PropertyType && 
                               !ifaceProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType);
          mappings.Add((sourceProp, ifaceProp, needsConversion));
        }
      }

      return mappings;
    });

    // Apply include/exclude filters
    IEnumerable<(PropertyInfo source, PropertyInfo target, bool needsConversion)> effectiveMappings = propertyMappings;
    if ((include != null && include.Count > 0) || (exclude != null && exclude.Count > 0))
    {
      effectiveMappings = propertyMappings
        .Where(m => (include == null || include.Count == 0 || include.Contains(m.source.Name)) &&
                    (exclude == null || !exclude.Contains(m.source.Name)));
    }

    var mappingsList = effectiveMappings.ToList();

    // Sequential property access - outer parallelism handles concurrency across items
    // This avoids N×M thread pool contention when combined with Parallel.ForEachAsync
    var expando = new ExpandoObject();
    var expandoDict = (IDictionary<string, object>)expando;
    
    foreach (var (source, target, needsConversion) in mappingsList)
    {
      object value = s_compiledGetters.TryGetValue(source, out var getter)
        ? getter(this)
        : source.GetValue(this);
      
      expandoDict[target.Name] = needsConversion 
        ? ConvertValueToTargetType(value, target.PropertyType) 
        : value;
    }

    return Task.FromResult((TInterface)BindExpandoToInterface(expando, interfaceType));
  }

  /// <summary>
  /// Converts a value to the specified target type, handling enum and primitive conversions.
  /// </summary>
  private static object ConvertValueToTargetType(object value, Type targetType)
  {
    if (value == null) return null;
    
    var valueType = value.GetType();
    
    // If types already match or are assignable, return as-is
    if (valueType == targetType || targetType.IsAssignableFrom(valueType))
      return value;
    
    // Handle enum conversions
    if (targetType.IsEnum)
    {
      if (valueType == typeof(string))
      {
        return Enum.Parse(targetType, (string)value);
      }
      else if (valueType.IsEnum)
      {
        // Convert between different enum types
        return Enum.ToObject(targetType, Convert.ToInt32(value));
      }
      else if (valueType.IsPrimitive || valueType == typeof(int))
      {
        // Convert from numeric to enum
        return Enum.ToObject(targetType, value);
      }
    }
    
    // Handle string target from enum
    if (targetType == typeof(string) && valueType.IsEnum)
    {
      return value.ToString();
    }
    
    // Handle nullable types
    var underlyingType = Nullable.GetUnderlyingType(targetType);
    if (underlyingType != null)
    {
      return ConvertValueToTargetType(value, underlyingType);
    }
    
    // Try general conversion
    try
    {
      return Convert.ChangeType(value, targetType);
    }
    catch
    {
      return value; // Return original if conversion fails
    }
  }

  /// <summary>
  /// Gets or creates a compiled getter delegate for the specified property.
  /// </summary>
  private static Func<object, object> GetOrCompileGetter(PropertyInfo property)
  {
    return s_compiledGetters.GetOrAdd(property, prop =>
    {
      // Create a compiled expression for fast property access
      var instanceParam = Expression.Parameter(typeof(object), "instance");
      var castInstance = Expression.Convert(instanceParam, prop.DeclaringType);
      var propertyAccess = Expression.Property(castInstance, prop);
      var boxedResult = Expression.Convert(propertyAccess, typeof(object));

      var lambda = Expression.Lambda<Func<object, object>>(boxedResult, instanceParam);
      return lambda.Compile();
    });
  }

  private static object BindExpandoToInterface(object obj, Type interfaceType)
  {
    if (obj == null) return null;
    if (interfaceType.IsInstanceOfType(obj)) return obj;

    // If it's an ExpandoObject and the target is an interface, bind it
    if (obj is System.Dynamic.ExpandoObject && interfaceType.IsInterface)
    {
      // Recursively bind all properties
      var expandoDict = (IDictionary<string, object>)obj;
      foreach (var prop in interfaceType.GetProperties())
      {
        if (expandoDict.TryGetValue(prop.Name, out var value) && value != null)
        {
          var valueType = value.GetType();
          var targetType = prop.PropertyType;

          // Handle collections of interfaces
          if (targetType.IsGenericType &&
              typeof(System.Collections.IEnumerable).IsAssignableFrom(targetType) &&
              targetType.GetGenericArguments()[0].IsInterface)
          {
            var elemType = targetType.GetGenericArguments()[0];
            if (value is System.Collections.IEnumerable enumerable)
            {
              var listType = typeof(List<>).MakeGenericType(elemType);
              var list = (System.Collections.IList)Activator.CreateInstance(listType);
              foreach (var item in enumerable)
              {
                list.Add(BindExpandoToInterface(item, elemType));
              }
              expandoDict[prop.Name] = list;
            }
          }
          // Handle nested interface
          else if (targetType.IsInterface && value is System.Dynamic.ExpandoObject)
          {
            expandoDict[prop.Name] = BindExpandoToInterface(value, targetType);
          }
          // Handle enum to string conversion
          else if (targetType == typeof(string) && valueType.IsEnum)
          {
            expandoDict[prop.Name] = value.ToString();
          }
          // Handle string to enum conversion
          else if (targetType.IsEnum && valueType == typeof(string))
          {
            expandoDict[prop.Name] = Enum.Parse(targetType, (string)value);
          }
          // Handle other type conversions (e.g., int to long, etc.)
          else if (targetType != valueType && !targetType.IsAssignableFrom(valueType))
          {
            try
            {
              expandoDict[prop.Name] = Convert.ChangeType(value, targetType);
            }
            catch
            {
              // Keep original value if conversion fails
            }
          }
        }
      }
      return TypeProxy.As(obj, interfaceType);
    }
    return obj;
  }
#endif
}

#if !MTGOSDKCORE
/// <summary>
/// Extension methods for batch serialization with combined parallelism.
/// </summary>
public static class SerializationExtensions
{
  /// <summary>
  /// Serializes all items in a collection with combined outer (across items) and inner (across properties) parallelism.
  /// This provides optimal I/O overlap for remote object serialization.
  /// </summary>
  /// <typeparam name="TSource">The source item type in the collection.</typeparam>
  /// <typeparam name="TInterface">The interface type to serialize to.</typeparam>
  /// <param name="source">The collection of items to process.</param>
  /// <param name="selector">A function to extract the serializable object from each item.</param>
  /// <param name="maxDegreeOfParallelism">Maximum number of items to process concurrently. Default is -1 (system default).</param>
  /// <returns>A task that resolves to a list of serialized objects.</returns>
  /// <example>
  /// <code>
  /// var dtos = await cards.SerializeAllAsync&lt;CardQuantityPair, ICardDTO&gt;(c => c.Card);
  /// </code>
  /// </example>
  public static async Task<List<TInterface>> SerializeAllAsync<TSource, TInterface>(
    this IEnumerable<TSource> source,
    Func<TSource, SerializableBase> selector,
    int maxDegreeOfParallelism = -1)
  {
    var items = source.ToList();
    var results = new TInterface[items.Count];
    
    var options = new ParallelOptions
    {
      MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount
    };
    
    await Parallel.ForEachAsync(
      Enumerable.Range(0, items.Count),
      options,
      async (index, cancellationToken) =>
      {
        var item = items[index];
        var serializable = selector(item);
        results[index] = await serializable.SerializeAsAsync<TInterface>().ConfigureAwait(false);
      }).ConfigureAwait(false);
    
    return results.ToList();
  }
  
  /// <summary>
  /// Serializes all items in a collection with combined parallelism, projecting additional data.
  /// </summary>
  /// <typeparam name="TSource">The source item type.</typeparam>
  /// <typeparam name="TInterface">The interface type to serialize to.</typeparam>
  /// <typeparam name="TResult">The result type including projected data.</typeparam>
  /// <param name="source">The collection of items.</param>
  /// <param name="selector">Function to extract the serializable object.</param>
  /// <param name="resultSelector">Function to combine source item with serialized result.</param>
  /// <param name="maxDegreeOfParallelism">Maximum concurrent items.</param>
  /// <returns>A task that resolves to projected results.</returns>
  /// <example>
  /// <code>
  /// var results = await cards.SerializeAllAsync&lt;CardQuantityPair, ICardDTO, (int Qty, ICardDTO Card)&gt;(
  ///   c => c.Card,
  ///   (c, dto) => (c.Quantity, dto)
  /// );
  /// </code>
  /// </example>
  public static async Task<List<TResult>> SerializeAllAsync<TSource, TInterface, TResult>(
    this IEnumerable<TSource> source,
    Func<TSource, SerializableBase> selector,
    Func<TSource, TInterface, TResult> resultSelector,
    int maxDegreeOfParallelism = -1)
  {
    var items = source.ToList();
    var results = new TResult[items.Count];
    
    var options = new ParallelOptions
    {
      MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount
    };
    
    await Parallel.ForEachAsync(
      Enumerable.Range(0, items.Count),
      options,
      async (index, cancellationToken) =>
      {
        var item = items[index];
        var serializable = selector(item);
        var dto = await serializable.SerializeAsAsync<TInterface>().ConfigureAwait(false);
        results[index] = resultSelector(item, dto);
      }).ConfigureAwait(false);
    
    return results.ToList();
  }
}
#endif
