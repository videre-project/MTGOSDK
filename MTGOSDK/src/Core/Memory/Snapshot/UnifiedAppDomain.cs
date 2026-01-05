/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Reflection;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace MTGOSDK.Core.Memory.Snapshot;

/// <summary>
/// Encapsulates access to all AppDomains in the process
/// </summary>
public class UnifiedAppDomain
{
  private readonly AppDomain[] _domains = [AppDomain.CurrentDomain];
  
  // Type resolution cache to avoid iterating all assemblies on every call
  private readonly ConcurrentDictionary<string, Type> _typeCache = new();

  public UnifiedAppDomain(SnapshotRuntime snapshot = null)
  {
    if (snapshot != null)
    {
      // Use snapshot's heap searching to locate all 'System.AppDomain' objects.
      try
      {
        var candidates = snapshot
          .GetHeapObjects(heapObjType =>
              heapObjType == typeof(AppDomain).FullName, true);

        _domains = candidates
          .Select(cand =>
              snapshot
                .GetHeapObject(cand.Address, false, cand.Type, cand.HashCode)
                .instance)
          .Cast<AppDomain>().ToArray();
      }
      catch// (Exception ex)
      {
        // Logger.Debug("[Diver][UnifiedAppDomain] Failed to search heap for runtime assemblies. Error: " + ex.Message);
      }
    }
  }

  public Assembly GetAssembly(string name)
  {
    return _domains.SelectMany(domain => domain.GetAssemblies())
      .Where(asm => asm.GetName().Name == name)
      .SingleOrDefault();
  }

  public Assembly[] GetAssemblies() =>
    _domains
      .SelectMany(domain => domain.GetAssemblies())
      .ToArray();

  public Type ResolveType(string typeFullName, string assemblyName = null)
  {
    // Check cache first (lock-free fast path)
    if (_typeCache.TryGetValue(typeFullName, out var cached))
      return cached;
    
    // Skip invalid type requests (like TInterface`1, Task`1 without namespace)
    // Don't cache these fallbacks - they may get fixed later with proper names
    // Check if this is a short name without namespace (no dots before any backtick)
    bool isShortName = !typeFullName.Contains('.') ||
                       (typeFullName.Contains('`') &&
                        !typeFullName.Substring(0, typeFullName.IndexOf('`')).Contains('.'));

    // Short names without namespace cannot be reliably resolved - return fallback
    if (isShortName)
      return typeof(object);

    // TODO: Nullable gets a special case but in general we should switch to a
    //       recursive type-resolution to account for types like:
    //           Dictionary<FirstAssembly.FirstType, SecondAssembly.SecondType>
    if (typeFullName.StartsWith("System.Nullable`1[["))
    {
      var result = ResolveNullableType(typeFullName, assemblyName);
      if (result != null)
        _typeCache[typeFullName] = result;
      return result;
    }

    var lookupName = typeFullName;
    if (lookupName.Contains('<') && lookupName.EndsWith(">"))
    {
      string genericParams = lookupName.Substring(lookupName.LastIndexOf('<'));
      int numOfParams = genericParams.Split(',').Length;

      string nonGenericPart = lookupName.Substring(0, lookupName.LastIndexOf('<'));
      lookupName = $"{nonGenericPart}`{numOfParams}";
    }

    foreach (Assembly asm in _domains.SelectMany(d => d.GetAssemblies()))
    {
      Type t = asm.GetType(lookupName, throwOnError: false);
      if (t != null)
      {
        _typeCache[typeFullName] = t;
        return t;
      }
    }

    throw new Exception(
        $"Could not find type '{typeFullName}' in any of the known assemblies");
  }

  public TypesDump ResolveTypes(string assemblyName)
  {
    Assembly matchingAssembly = _domains
      .SelectMany(d => d.GetAssemblies())
      .Where(asm => asm.GetName().Name == assemblyName)
      .SingleOrDefault();

    if (matchingAssembly == null)
      throw new Exception($"No assemblies were found matching '{assemblyName}'.");

    List<TypesDump.TypeIdentifiers> types = new();
    foreach (Type type in matchingAssembly.GetTypes())
    {
      types.Add(new TypesDump.TypeIdentifiers() { TypeName = type.FullName });
    }

    return new TypesDump() { AssemblyName = assemblyName, Types = types };
  }

  private Type ResolveNullableType(string typeFullName, string assemblyName)
  {
    // Remove prefix: "System.Nullable`1[["
    string innerTypeName = typeFullName.Substring("System.Nullable`1[[".Length);
    // Remove suffix: "]]"
    innerTypeName = innerTypeName.Substring(0, innerTypeName.Length - 2);
    // Type name is everything before the first comma (after that we have some assembly info)
    innerTypeName = innerTypeName.Substring(0, innerTypeName.IndexOf(',')).Trim();

    Type innerType = ResolveType(innerTypeName);
    if (innerType == null) return null;

    Type nullable = typeof(Nullable<>);
    return nullable.MakeGenericType(innerType);
  }
}
