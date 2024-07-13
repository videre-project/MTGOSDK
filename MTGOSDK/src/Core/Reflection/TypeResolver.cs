﻿/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;

using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Resolves local and remote types. Contains a cache so the same TypeFullName
/// object is returned for different resolutions for the same remote type.
/// </summary>
public class TypeResolver()
{
  private readonly Dictionary<Tuple<string, string>, Type> _cache = new();

  // Since the resolver works with a cache that should be global we make the
  // whole class a singleton
  public static TypeResolver Instance = new TypeResolver();

  public void RegisterType(Type type)
    => RegisterType(type.Assembly.GetName().Name, type.FullName, type);

  public void RegisterType(string assemblyName, string typeFullName, Type type)
  {
    _cache[new Tuple<string, string>(assemblyName, typeFullName)] = type;
  }

  public Type Resolve(string assemblyName, string typeFullName)
  {
    // Start by searching cache
    if (_cache.TryGetValue(new Tuple<string, string>(assemblyName, typeFullName),
                           out Type resolvedType))
    {
      return resolvedType;
    }

    // Search for locally available types
    // EXCEPT for enums because that breaks RemoteEnum
    IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies();
    // Filter assemblies but avoid filtering for "mscorlib"
    if(assemblyName?.Equals("mscorlib") == false)
    {
      assemblies = assemblies.Where(asm => asm.FullName.Contains(assemblyName ?? ""));
    }

    foreach (Assembly assembly in assemblies)
    {
      resolvedType = assembly.GetType(typeFullName);
      if(resolvedType != null)
      {
        // Found the type!
        // But retreat if it's an enum (and get remote proxy of it instead)
        if(resolvedType.IsEnum) resolvedType = null;
        break;
      }
    }

    if (resolvedType != null && resolvedType is RemoteType)
    {
      RegisterType(assemblyName, typeFullName, resolvedType);
    }

    return resolvedType;
  }

  public void ClearCache()
  {
    _cache.Clear();
  }
}
