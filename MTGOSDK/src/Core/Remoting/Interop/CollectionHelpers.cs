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
  /// Counts items in a collection in the remote process.
  /// </summary>
  public static int Count(object collection)
  {
    return collection is ICollection items
      ? items.Count
      : ((IEnumerable)collection).Cast<object>().Count();
  }

  /// <summary>
  /// Filters a collection to items whose runtime type is assignable to a type.
  /// </summary>
  /// <param name="collection">The collection to filter.</param>
  /// <param name="typeFullName">The full name of the required type.</param>
  /// <returns>A list of assignable items.</returns>
  public static List<object> WhereAssignableTo(
    object collection,
    string typeFullName)
  {
    var result = new List<object>();

    foreach (var item in (IEnumerable)collection)
    {
      if (item is null) continue;

      var type = item.GetType();
      if (IsAssignableTo(type, typeFullName))
        result.Add(item);
    }

    return result;
  }

  private static bool IsAssignableTo(Type type, string typeFullName) =>
    type.FullName == typeFullName ||
    type.GetInterfaces().Any(i => i.FullName == typeFullName) ||
    (type.BaseType is not null && IsAssignableTo(type.BaseType, typeFullName));

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
  /// Filters a collection by comparing an enum-like property by name.
  /// </summary>
  public static List<object> WherePropertyEnumName(
    object collection,
    string propertyName,
    string enumName)
  {
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
      if (string.Equals(propValue?.ToString(), enumName, StringComparison.OrdinalIgnoreCase))
        result.Add(item);
    }

    return result;
  }

  /// <summary>
  /// Filters a collection by testing whether a string property contains a value.
  /// Supports dotted property paths such as <c>Poster.Name</c>.
  /// </summary>
  public static List<object> WherePropertyStringContains(
    object collection,
    string propertyPath,
    string value,
    bool ignoreCase = true)
  {
    var result = new List<object>();
    var comparison = ignoreCase
      ? StringComparison.OrdinalIgnoreCase
      : StringComparison.Ordinal;

    foreach (var item in (IEnumerable)collection)
    {
      if (item is null) continue;

      var propValue = GetPropertyPathValue(item, propertyPath)?.ToString();
      if (propValue?.IndexOf(value, comparison) >= 0)
        result.Add(item);
    }

    return result;
  }

  private static object? GetPropertyPathValue(
    object item,
    string propertyPath)
  {
    object? current = item;
    foreach (var segment in propertyPath.Split('.'))
    {
      if (current is null) return null;

      var property = current.GetType().GetProperty(segment)
        ?? throw new ArgumentException(
          $"Property '{segment}' not found on type '{current.GetType().Name}'");

      current = property.GetValue(current);
    }

    return current;
  }

  /// <summary>
  /// Orders a collection by a property value.
  /// </summary>
  /// <param name="collection">The collection to sort.</param>
  /// <param name="propertyName">The name or dotted path of the property to sort by.</param>
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
    object GetPropertyValue(object item)
    {
      if (item is null) return 0; // Treat nulls as default/min value? Or handle gracefully.
      return GetPropertyPathValue(item, propertyName);
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
  /// Computes property-hash pairs for every ThingElement in a collection.
  /// Executes fully in-process (injected into the remote target), so every
  /// property access is a direct in-memory call with no IPC overhead.
  /// </summary>
  /// <param name="collection">
  /// The ThingElement collection (e.g. <c>GamePlayStatusMessageDetailed.ThingElements</c>).
  /// </param>
  /// <param name="excludedNamesCsv">
  /// Runtime <c>MagicProperty</c> member names to EXCLUDE from the hash,
  /// delimited by <c>|</c>. All other properties are included.
  /// </param>
  /// <returns>
  /// Flat <c>int[]</c> of <c>[thingId₀, hash₀, thingId₁, hash₁, …]</c>.
  /// </returns>
  public static int[] ComputeThingSnapshotHashes(object collection, string excludedNamesCsv)
  {
    var excludeSet = !string.IsNullOrWhiteSpace(excludedNamesCsv)
      ? new HashSet<string>(excludedNamesCsv.Split(['|'], StringSplitOptions.RemoveEmptyEntries))
      : new HashSet<string>();

    // Pre-allocate with expected capacity (2 ints per element).
    var items = collection as ICollection;
    var results = new List<int>(items != null ? items.Count * 2 : 64);

    // Reuse accessor cache across all elements (container types are homogeneous).
    var accessorCache = new Dictionary<Type, AccessorSet>();

    // Cache PropertyInfo for ThingElement.Properties (all elements share the same type).
    PropertyInfo propsPiCached = null;
    Type lastElementType = null;

    foreach (var element in (IEnumerable)collection)
    {
      if (element == null)
      {
        results.Add(-1); // sentinel: null element
        results.Add(-1);
        continue;
      }

      // Cache the Properties PropertyInfo lookup by element type.
      var elementType = element.GetType();
      if (elementType != lastElementType)
      {
        propsPiCached = elementType
          .GetProperty("Properties", BindingFlags.Instance | BindingFlags.NonPublic);
        lastElementType = elementType;
      }
      if (propsPiCached == null)
      {
        results.Add(-2); // sentinel: no Properties property
        results.Add(0);
        continue;
      }

      var propsObj = propsPiCached.GetValue(element);
      if (propsObj == null)
      {
        results.Add(-3); // sentinel: Properties returned null
        results.Add(0);
        continue;
      }

      int thingId = 0;
      int hash = ComputeContainerHashExcluding(
        propsObj, excludeSet, ref thingId, accessorCache);

      results.Add(thingId);
      results.Add(hash);
    }

    return results.ToArray();
  }

  /// <summary>
  /// Gets a dictionary property from a PropertyContainer,
  /// trying public accessor then private backing field.
  /// </summary>
  private static object GetDictProperty(
    Type ct, object container, string publicName, string privateName)
  {
    const BindingFlags pub  = BindingFlags.Instance | BindingFlags.Public;
    const BindingFlags priv = BindingFlags.Instance | BindingFlags.NonPublic;

    var obj = ct.GetProperty(publicName, pub)?.GetValue(container);
    if (obj != null) return obj;

    return ct.GetProperty(privateName, priv)?.GetValue(container);
  }

  /// <summary>
  /// Cached per-container-type accessor set (int, string, sub dictionaries).
  /// All ThingElements share the same PropertyContainer type, so this is
  /// resolved once and reused for every element in the snapshot.
  /// </summary>
  private sealed class AccessorSet
  {
    public DictTypeInfo IntInfo;
    public DictTypeInfo StrInfo;
    public DictTypeInfo SubInfo;

    public object ThingNumberEnumKey;
    public bool ThingNumberResolved;

    /// <summary>
    /// Pre-parsed exclusion enum keys per dictionary type.
    /// </summary>
    public HashSet<object> IntExcluded = new();
    public HashSet<object> StrExcluded = new();
    public HashSet<object> SubExcluded = new();
  }

  /// <summary>
  /// Cached reflection info for a dictionary type (MethodInfo + enum key type).
  /// </summary>
  private sealed class DictTypeInfo
  {
    public MethodInfo TryGetValue;
    public Type KeyType;

    /// <summary>
    /// Cached GetEnumerator method for iterating dictionary entries.
    /// </summary>
    public MethodInfo GetEnumerator;
  }

  /// <summary>
  /// Resolves type info from a dictionary object. Called once per dict type.
  /// </summary>
  private static DictTypeInfo ResolveTypeInfo(object dictObj)
  {
    if (dictObj == null) return null;

    var dictType = dictObj.GetType();
    var tryGet = dictType.GetMethod("TryGetValue");
    if (tryGet == null) return null;

    // Discover enum key type from generic arguments
    Type keyType = null;
    if (dictType.IsGenericType)
    {
      keyType = dictType.GetGenericArguments()[0];
    }
    else
    {
      foreach (var iface in dictType.GetInterfaces())
      {
        if (!iface.IsGenericType) continue;
        var gtd = iface.GetGenericTypeDefinition();
        if (gtd == typeof(IDictionary<,>))
        {
          keyType = iface.GetGenericArguments()[0];
          break;
        }
      }
    }

    if (keyType == null || !keyType.IsEnum) return null;

    var getEnum = dictType.GetMethod("GetEnumerator");

    return new DictTypeInfo { TryGetValue = tryGet, KeyType = keyType, GetEnumerator = getEnum };
  }

  /// <summary>
  /// Builds or retrieves the AccessorSet for a container type, pre-parsing
  /// exclusion enum keys from the exclude set once.
  /// </summary>
  private static AccessorSet GetOrCreateAccessorSet(
    object container, HashSet<string> excludeSet, Dictionary<Type, AccessorSet> cache)
  {
    var ct = container.GetType();
    if (cache.TryGetValue(ct, out var set))
      return set;

    set = new AccessorSet();

    // Resolve type info for each dictionary kind
    var intObj = GetDictProperty(ct, container, "IntegerProperties", "IntegerPropertiesDictionary");
    set.IntInfo = ResolveTypeInfo(intObj);

    var strObj = GetDictProperty(ct, container, "StringProperties", "StringPropertiesDictionary");
    set.StrInfo = ResolveTypeInfo(strObj);

    var subObj = GetDictProperty(ct, container, "SubProperties", "SubPropertiesDictionary");
    set.SubInfo = ResolveTypeInfo(subObj);

    // Pre-parse ThingNumber enum key
    if (set.IntInfo != null)
    {
      try
      {
        set.ThingNumberEnumKey = Enum.Parse(set.IntInfo.KeyType, "THINGNUMBER", false);
        set.ThingNumberResolved = true;
      }
      catch { set.ThingNumberResolved = false; }
    }

    // Pre-parse exclusion names into enum keys for each dict type
    foreach (var name in excludeSet)
    {
      if (set.IntInfo != null)
      {
        try { set.IntExcluded.Add(Enum.Parse(set.IntInfo.KeyType, name, false)); }
        catch { }
      }
      if (set.StrInfo != null)
      {
        try { set.StrExcluded.Add(Enum.Parse(set.StrInfo.KeyType, name, false)); }
        catch { }
      }
      if (set.SubInfo != null)
      {
        try { set.SubExcluded.Add(Enum.Parse(set.SubInfo.KeyType, name, false)); }
        catch { }
      }
    }

    cache[ct] = set;
    return set;
  }

  /// <summary>
  /// Hashes a <c>PropertyContainer</c> by iterating ALL properties and
  /// excluding those in <paramref name="excludeSet"/>.
  /// Also extracts ThingNumber from integer properties.
  /// </summary>
  private static int ComputeContainerHashExcluding(
    object container, HashSet<string> excludeSet, ref int thingId,
    Dictionary<Type, AccessorSet> accessorCache)
  {
    int hash = 0;
    var set = GetOrCreateAccessorSet(container, excludeSet, accessorCache);
    var ct = container.GetType();

    // Integer properties — iterate all, skip excluded
    if (set.IntInfo != null)
    {
      var intObj = GetDictProperty(ct, container, "IntegerProperties", "IntegerPropertiesDictionary");
      if (intObj != null)
      {
        // Extract ThingNumber first
        if (set.ThingNumberResolved)
        {
          var args = new object[] { set.ThingNumberEnumKey, null };
          if ((bool)set.IntInfo.TryGetValue.Invoke(intObj, args) && args[1] != null)
            thingId = Convert.ToInt32(args[1]);
        }

        // Iterate all entries
        foreach (var entry in (IEnumerable)intObj)
        {
          var entryType = entry.GetType();
          var key = entryType.GetProperty("Key").GetValue(entry);
          if (set.IntExcluded.Contains(key)) continue;

          var val = entryType.GetProperty("Value").GetValue(entry);
          if (val != null)
            hash ^= unchecked(key.GetHashCode() * 397) ^ Convert.ToInt32(val);
        }
      }
    }

    // String properties — iterate all, skip excluded
    if (set.StrInfo != null)
    {
      var strObj = GetDictProperty(ct, container, "StringProperties", "StringPropertiesDictionary");
      if (strObj != null)
      {
        foreach (var entry in (IEnumerable)strObj)
        {
          var entryType = entry.GetType();
          var key = entryType.GetProperty("Key").GetValue(entry);
          if (set.StrExcluded.Contains(key)) continue;

          var val = entryType.GetProperty("Value").GetValue(entry);
          if (val != null)
            hash ^= unchecked(key.GetHashCode() * 397) ^ val.GetHashCode();
        }
      }
    }

    // Sub-property containers (e.g. COUNTERS_LIST) — iterate all, skip excluded
    if (set.SubInfo != null)
    {
      var subObj = GetDictProperty(ct, container, "SubProperties", "SubPropertiesDictionary");
      if (subObj != null)
      {
        foreach (var entry in (IEnumerable)subObj)
        {
          var entryType = entry.GetType();
          var key = entryType.GetProperty("Key").GetValue(entry);
          if (set.SubExcluded.Contains(key)) continue;

          var val = entryType.GetProperty("Value").GetValue(entry);
          if (val != null)
          {
            int dummy = 0;
            int subHash = ComputeContainerHashAll(val, ref dummy, accessorCache);
            hash ^= unchecked(key.GetHashCode() * 397) ^ subHash;
          }
        }
      }
    }

    return hash;
  }

  /// <summary>
  /// Hashes a <c>PropertyContainer</c> with no exclusions (hash all).
  /// Uses ToString().GetHashCode() for full coverage.
  /// Used for recursive sub-containers (e.g. COUNTERS_LIST).
  /// </summary>
  private static int ComputeContainerHashAll(object container, ref int thingId,
    Dictionary<Type, AccessorSet> accessorCache)
  {
    var ct = container.GetType();

    // Still extract thingId via TryGetValue using cached type info
    if (!accessorCache.TryGetValue(ct, out var set))
    {
      var intObj = GetDictProperty(ct, container, "IntegerProperties", "IntegerPropertiesDictionary");
      var intInfo = ResolveTypeInfo(intObj);
      if (intInfo != null)
      {
        object tnKey = null;
        try { tnKey = Enum.Parse(intInfo.KeyType, "THINGNUMBER", false); } catch { }
        set = new AccessorSet { IntInfo = intInfo, ThingNumberEnumKey = tnKey, ThingNumberResolved = tnKey != null };
        accessorCache[ct] = set;
      }
    }

    if (set?.IntInfo != null && set.ThingNumberResolved)
    {
      var intObj = GetDictProperty(ct, container, "IntegerProperties", "IntegerPropertiesDictionary");
      if (intObj != null)
      {
        var args = new object[] { set.ThingNumberEnumKey, null };
        if ((bool)set.IntInfo.TryGetValue.Invoke(intObj, args) && args[1] != null)
          thingId = Convert.ToInt32(args[1]);
      }
    }

    string text = container.ToString();
    return text?.GetHashCode() ?? 0;
  }

  /// <summary>
  /// Computes a hash for each item in a collection using its reflected properties.
  /// </summary>
  /// <param name="collection">The collection to hash.</param>
  /// <returns>An array of hash codes corresponding to each item.</returns>
  public static int[] ComputeHashList(IEnumerable collection)
  {
    var results = new List<int>();
    var propertyCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

    string[] propertiesToHash = null!;
    foreach (var item in collection)
    {
      if (item == null)
      {
        results.Add(-1);
        continue;
      }

      // If our properties to hash are not yet determined, infer them from the first item.
      if (propertiesToHash == null)
      {
        var type1 = item.GetType();
        var props1 = type1.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        propertyCache[type1] = props1.ToDictionary(p => p.Name, p => p);
        propertiesToHash = props1.Select(p => p.Name).ToArray();
      }

      int hash;
      var type = item.GetType();
      if (!propertyCache.TryGetValue(type, out var props))
      {
        props = new Dictionary<string, PropertyInfo>();
        foreach (var propName in propertiesToHash)
        {
          var prop = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
          if (prop != null)
            props.Add(propName, prop);
        }
        propertyCache[type] = props;
      }

      var hc = new HashCode();
      foreach (var kvp in props)
      {
        var val = kvp.Value.GetValue(item);
        hc.Add(val);
      }
      hash = hc.ToHashCode();

      results.Add(hash);
    }

    return results.ToArray();
  }

  /// <summary>
  /// Computes a hash for each item in a collection using selected property paths.
  /// </summary>
  /// <param name="collection">The collection to hash.</param>
  /// <param name="propertyPathsCsv">
  /// Comma- or pipe-separated property paths to include, such as
  /// <c>MatchId|Status|IsCompleted|CurrentGame.Id</c>.
  /// </param>
  /// <returns>An array of hash codes corresponding to each item.</returns>
  public static int[] ComputeHashList(
    IEnumerable collection,
    string propertyPathsCsv)
  {
    var propertyPaths = propertyPathsCsv
      .Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
      .Select(path => path.Trim())
      .Where(path => path.Length > 0)
      .ToArray();
    if (propertyPaths.Length == 0)
    {
      return ComputeHashList(collection);
    }

    var results = new List<int>();
    var memberCache = new Dictionary<(Type Type, string Name), MemberInfo?>();

    foreach (var item in collection)
    {
      if (item == null)
      {
        results.Add(-1);
        continue;
      }

      var hc = new HashCode();
      foreach (string propertyPath in propertyPaths)
      {
        hc.Add(GetPropertyPathValue(item, propertyPath, memberCache));
      }

      results.Add(hc.ToHashCode());
    }

    return results.ToArray();
  }

  private static object? GetPropertyPathValue(
    object item,
    string propertyPath,
    Dictionary<(Type Type, string Name), MemberInfo?> memberCache)
  {
    object? current = item;
    foreach (string memberName in propertyPath
               .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(name => name.Trim())
               .Where(name => name.Length > 0))
    {
      if (current is null)
      {
        return null;
      }

      Type currentType = current.GetType();
      MemberInfo? member = GetHashMember(currentType, memberName, memberCache);
      current = member switch
      {
        PropertyInfo property => property.GetValue(current),
        FieldInfo field => field.GetValue(current),
        _ => throw new MissingMemberException(
          currentType.FullName,
          memberName)
      };
    }

    return current;
  }

  private static MemberInfo? GetHashMember(
    Type type,
    string memberName,
    Dictionary<(Type Type, string Name), MemberInfo?> memberCache)
  {
    var key = (type, memberName);
    if (memberCache.TryGetValue(key, out var cachedMember))
    {
      return cachedMember;
    }

    const BindingFlags flags =
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    MemberInfo? member =
      type.GetProperty(memberName, flags) ??
      (MemberInfo?)type.GetField(memberName, flags);
    memberCache[key] = member;
    return member;
  }
}
