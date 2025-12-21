/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace MTGOSDK.Core.Memory.Snapshot;

/// <summary>
/// Encapsulates access to all AppDomains in the process
/// </summary>
public class UnifiedAppDomain
{
  private readonly AppDomain[] _domains = [AppDomain.CurrentDomain];

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
    // Skip invalid type requests
    if (typeFullName.StartsWith("TInterface`")) return typeof(object);

    // TODO: Nullable gets a special case but in general we should switch to a
    //       recursive type-resolution to account for types like:
    //           Dictionary<FirstAssembly.FirstType, SecondAssembly.SecondType>
    if (typeFullName.StartsWith("System.Nullable`1[["))
    {
      return ResolveNullableType(typeFullName, assemblyName);
    }

    if (typeFullName.Contains('<') && typeFullName.EndsWith(">"))
    {
      string genericParams = typeFullName.Substring(typeFullName.LastIndexOf('<'));
      int numOfParams = genericParams.Split(',').Length;

      string nonGenericPart = typeFullName.Substring(0,typeFullName.LastIndexOf('<'));
      // TODO: Does this event work? it turns List<int> and List<string> both to List`1?
      typeFullName = $"{nonGenericPart}`{numOfParams}";
    }

    foreach (Assembly asm in _domains.SelectMany(d => d.GetAssemblies()))
    {
      Type t = asm.GetType(typeFullName, throwOnError: false);
      if (t != null)
        return t;
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
    if(innerType == null)
      return null;

    Type nullable = typeof(Nullable<>);
    return nullable.MakeGenericType(innerType);
  }
}
