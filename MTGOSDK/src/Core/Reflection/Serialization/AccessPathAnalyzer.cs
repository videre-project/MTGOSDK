/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;


namespace MTGOSDK.Core.Reflection.Serialization;

/// <summary>
/// Analyzes DLRWrapper types to extract remote access paths for batch fetching.
/// </summary>
public static class AccessPathAnalyzer
{
  /// <summary>
  /// Cache of analyzed access paths per (wrapper type, interface type).
  /// </summary>
  private static readonly Dictionary<(Type, Type), string[]> s_pathCache = new();
  private static readonly object s_lock = new();

  // Cached reference to the source-generated registry type (may be null if not generated)
  private static Type? s_registryType;
  private static MethodInfo? s_getPathsMethod;
  private static bool s_registryChecked;

  /// <summary>
  /// Gets batchable access paths for properties that exist in both the wrapper
  /// and the target interface.
  /// </summary>
  /// <typeparam name="TWrapper">The DLRWrapper type.</typeparam>
  /// <typeparam name="TInterface">The serialization interface type.</typeparam>
  /// <returns>Array of remote access paths that can be batch-fetched.</returns>
  public static string[] GetBatchablePathsForInterface<TWrapper, TInterface>()
    => GetBatchablePathsForInterface(typeof(TWrapper), typeof(TInterface));

  /// <summary>
  /// Gets batchable access paths for properties that exist in both the wrapper
  /// and the target interface.
  /// </summary>
  public static string[] GetBatchablePathsForInterface(Type wrapperType, Type interfaceType)
  {
    var key = (wrapperType, interfaceType);
    
    lock (s_lock)
    {
      if (s_pathCache.TryGetValue(key, out var cached))
        return cached;
    }

    var paths = AnalyzePaths(wrapperType, interfaceType);
    
    lock (s_lock)
    {
      s_pathCache[key] = paths;
    }
    
    return paths;
  }

  /// <summary>
  /// Gets a reverse mapping from remote paths back to interface property names.
  /// Used to translate batch response keys back to interface property names.
  /// </summary>
  /// <param name="wrapperType">The DLRWrapper type.</param>
  /// <param name="interfaceType">The serialization interface type.</param>
  /// <returns>Dictionary mapping remote paths to interface property names.</returns>
  public static Dictionary<string, string> GetReversePathMap(Type wrapperType, Type interfaceType)
  {
    var reverseMap = new Dictionary<string, string>();
    
    // Get interface properties
    var interfaceProps = interfaceType.GetProperties(
      BindingFlags.Public | BindingFlags.Instance)
      .Select(p => p.Name)
      .ToHashSet();

    // Get property maps from registry (walking inheritance chain)
    var allMaps = new List<Dictionary<string, string>>();
    var currentType = wrapperType;
    while (currentType != null && currentType != typeof(object))
    {
      var map = GetPropertyMapFromRegistry(currentType);
      if (map != null && map.Count > 0)
      {
        allMaps.Add(map);
      }
      currentType = currentType.BaseType;
    }

    if (allMaps.Count > 0)
    {
      // Merge all maps (derived class properties override base class)
      var mergedMap = new Dictionary<string, string>();
      for (int i = allMaps.Count - 1; i >= 0; i--)
      {
        foreach (var kvp in allMaps[i])
        {
          mergedMap[kvp.Key] = kvp.Value;
        }
      }
      
      // Build reverse map for interface properties only
      foreach (var propName in interfaceProps)
      {
        if (mergedMap.TryGetValue(propName, out var path))
        {
          reverseMap[path] = propName;
        }
      }
    }

    return reverseMap;
  }

  private static string[] AnalyzePaths(Type wrapperType, Type interfaceType)
  {
    // Get interface properties
    var interfaceProps = interfaceType.GetProperties(
      BindingFlags.Public | BindingFlags.Instance)
      .Select(p => p.Name)
      .ToHashSet();

    // Try to get property map first, walking the inheritance hierarchy
    var allMaps = new List<Dictionary<string, string>>();
    
    // Walk up the inheritance chain collecting property maps
    var currentType = wrapperType;
    while (currentType != null && currentType != typeof(object))
    {
      var map = GetPropertyMapFromRegistry(currentType);
      if (map != null && map.Count > 0)
      {
        allMaps.Add(map);
      }
      currentType = currentType.BaseType;
    }
    
    if (allMaps.Count > 0)
    {
      // Merge all maps (derived class properties override base class if same name)
      var mergedMap = new Dictionary<string, string>();
      // Process in reverse order so derived class properties override base
      for (int i = allMaps.Count - 1; i >= 0; i--)
      {
        foreach (var kvp in allMaps[i])
        {
          mergedMap[kvp.Key] = kvp.Value;
        }
      }
      
      // Filter to interface properties AND primitive types only
      // Complex types can't be batch-serialized properly and should fall through to DRO
      // We check the WRAPPER's property type, not the interface type, because:
      // - ITournament.Format is string, but Tournament.Format is PlayFormat (complex)
      // - The batch fetch can't handle this conversion so we need to skip it
      var wrapperPropsDict = new Dictionary<string, Type?>();
      var searchType = wrapperType;
      while (searchType != null && searchType != typeof(object))
      {
        foreach (var prop in searchType.GetProperties(
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
          if (!wrapperPropsDict.ContainsKey(prop.Name))
          {
            wrapperPropsDict[prop.Name] = prop.PropertyType;
          }
        }
        searchType = searchType.BaseType;
      }
        
      var resultPaths = new HashSet<string>();
      foreach (var propName in interfaceProps)
      {
        if (mergedMap.TryGetValue(propName, out var path))
        {
          // Check if the WRAPPER property type is serializable (primitive-ish)
          // If wrapper has a complex type, skip even if interface expects string
          if (wrapperPropsDict.TryGetValue(propName, out var propType) && propType != null)
          {
            if (!IsBatchSerializableType(propType))
              continue;  // Skip complex types like PlayFormat, EventStructure, etc.
          }
          resultPaths.Add(path);
        }
      }
      return resultPaths.ToArray();
    }

    // Fallback to old behavior (filtering by prefix) if map not available
    var allPaths = GetPathsFromRegistry(wrapperType);
    if (allPaths.Length == 0)
      return Array.Empty<string>();

    return allPaths
      .Where(p => 
      {
        var firstSegment = p.Split('.')[0];
        return interfaceProps.Contains(firstSegment);
      })
      .ToArray();
  }

  private static MethodInfo? s_getPropertyMapMethod;

  private static Dictionary<string, string>? GetPropertyMapFromRegistry(Type wrapperType)
  {
    if (!s_registryChecked)
    {
      InitializeRegistry();
    }

    if (s_getPropertyMapMethod == null) return null;

    try
    {
      // Try the actual type first
      var result = s_getPropertyMapMethod.Invoke(null, new object[] { wrapperType });
      var map = result as Dictionary<string, string>;
      
      // If not found and this is a constructed generic type, try the generic type definition with formatted name       
      if ((map == null || map.Count == 0) && wrapperType.IsGenericType)
      {
        // Convert from .NET format (CollectionItem`1) to Roslyn format (CollectionItem<T>)
        var formattedName = FormatGenericTypeName(wrapperType.IsGenericTypeDefinition ? wrapperType : wrapperType.GetGenericTypeDefinition());
        
        // Try looking up by formatted name directly via reflection on _paths dictionary
        var pathsField = s_registryType?.GetField("_paths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (pathsField != null)
        {
          var pathsDict = pathsField.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
          if (pathsDict != null && pathsDict.TryGetValue(formattedName, out map))
          {
            return map;
          }
        }
      }
      
      return map;
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  /// Converts a .NET generic type name from backtick notation (`1) to angle bracket notation (&lt;T&gt;)
  /// to match Roslyn's ToDisplayString() format used in the generated registry.
  /// </summary>
  private static string FormatGenericTypeName(Type genericType)
  {
    if (!genericType.IsGenericType)
      return genericType.FullName ?? genericType.Name;
    
    // Get the type without generic arguments
    var fullName = genericType.FullName ?? genericType.Name;
    
    // Remove everything after the backtick
    var backtickIndex = fullName.IndexOf('`');
    if (backtickIndex >= 0)
    {
      var baseName = fullName.Substring(0, backtickIndex);
      var genericParams = genericType.GetGenericArguments();
      var paramNames = string.Join(", ", genericParams.Select((_, i) => 
        i < 26 ? ((char)('T' + (i == 0 ? 0 : i))).ToString() : $"T{i}"));
      return $"{baseName}<{paramNames}>";
    }
    
    return fullName;
  }

  private static void InitializeRegistry()
  {
      s_registryChecked = true;
      s_registryType = Type.GetType(
        "MTGOSDK.Core.Reflection.Serialization.RemoteAccessPathRegistry, MTGOSDK");
      if (s_registryType != null)
      {
        s_getPathsMethod = s_registryType.GetMethod("GetPaths", new[] { typeof(Type) });
        s_getPropertyMapMethod = s_registryType.GetMethod("GetPropertyMap", new[] { typeof(Type) });
      }
  }

  /// <summary>
  /// Gets paths from the source-generated RemoteAccessPathRegistry via reflection.
  /// Returns empty array if the generated type doesn't exist.
  /// </summary>
  private static string[] GetPathsFromRegistry(Type wrapperType)
  {
    if (!s_registryChecked)
    {
      InitializeRegistry();
    }

    if (s_getPathsMethod == null)
      return Array.Empty<string>();

    try
    {
      var result = s_getPathsMethod.Invoke(null, new object[] { wrapperType });
      return result as string[] ?? Array.Empty<string>();
    }
    catch
    {
      return Array.Empty<string>();
    }
  }

  /// <summary>
  /// Clears the analysis cache. Used for testing.
  /// </summary>
  public static void ClearCache()
  {
    lock (s_lock)
    {
      s_pathCache.Clear();
    }
  }
  
  /// <summary>
  /// Determines if a type can be batch-serialized (primitives, strings, enums, DateTime, etc.)
  /// Complex types like DLRWrapper subclasses should fall through to DRO access.
  /// </summary>
  private static bool IsBatchSerializableType(Type type)
  {
    // Unwrap nullable types
    var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
    
    return underlyingType.IsPrimitive ||
           underlyingType == typeof(string) ||
           underlyingType == typeof(DateTime) ||
           underlyingType == typeof(TimeSpan) ||
           underlyingType == typeof(decimal) ||
           underlyingType == typeof(Guid) ||
           underlyingType.IsEnum;
  }
}
