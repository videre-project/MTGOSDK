﻿/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Reflection;

using MTGOSDK.Core.Remoting.Interop.Extensions;


namespace MTGOSDK.Core.Remoting.Interop.Utils;

public static class TypeExt
{
  public class WildCardEnabledTypesComparer : IEqualityComparer<Type>
  {
    public bool Equals(Type x, Type y)
    {
      if (x is WildCardType || y is WildCardType)
        return true;
      return x.IsAssignableFrom(y);
    }

    public int GetHashCode(Type obj) => obj.GetHashCode();
  }

  private static readonly WildCardEnabledTypesComparer _wildCardTypesComparer = new();

  /// <summary>
  /// Searches a type for a specific method. If not found searches its ancestors.
  /// </summary>
  /// <param name="t">The type to search</param>
  /// <param name="methodName">Method name</param>
  /// <param name="parameterTypes">Types of parameters in the function, in order.</param>
  /// <returns>MethodInfo of the method if found, null otherwise.</returns>
  public static MethodInfo? GetMethodRecursive(
    this Type t,
    string methodName,
    Type[]? parameterTypes = null)
  => GetMethodRecursive(t, methodName, null, parameterTypes);

  /// <summary>
  /// Searches a type for a specific method. If not found searches its ancestors.
  /// </summary>
  /// <param name="t">The type to search</param>
  /// <param name="methodName">Method name</param>
  /// <param name="genericArgumentTypes">Types of generic arguments in the function, in order.</param>
  /// <param name="parameterTypes">Types of parameters in the function, in order.</param>
  /// <returns>MethodInfo of the method if found, null otherwise.</returns>
  public static MethodInfo? GetMethodRecursive(
    this Type t,
    string methodName,
    Type[]? genericArgumentTypes,
    Type[]? parameterTypes)
  {
    // Find all methods with the given name
    var methods = t.GetMethods((BindingFlags)0xffff)
      .Where(m => m.Name == methodName);

    // Filter methods by generic argument types (if provided)
    if (genericArgumentTypes != null && genericArgumentTypes.Length > 0)
    {
      methods = methods
        .Where(m => m.ContainsGenericParameters == true)
        .Where(m => m.GetGenericArguments().Length == genericArgumentTypes.Length)
        .Select(m => m.MakeGenericMethod(genericArgumentTypes));
    }

    MethodInfo method = default;
    // If no argument types are provided there exists only one possible method.
    if (parameterTypes == null)
    {
      method = methods.SingleOrDefault();
    }
    // Filter methods by parameter types to find an exact match
    else
    {
      MethodInfo[]? exactMatches = methods.Where(m =>
          m.GetParameters()
            .Select(pi => pi.ParameterType)
            .SequenceEqual(parameterTypes))
        .ToArray();
      if (exactMatches != null && exactMatches.Length == 1)
      {
        method = exactMatches.First();
      }
      else
      {
        // Do a less strict search
        method = methods.SingleOrDefault(m =>
            m.GetParameters()
              .Select(pi => pi.ParameterType)
              .SequenceEqual(parameterTypes, _wildCardTypesComparer));
      }
    }

    if (method != null)
      return method;

    // Not found in this type...
    if (t == typeof(object))
      return null; // No more parents

    // Check parent (until `object`)
    return t.BaseType.GetMethodRecursive(methodName, parameterTypes);
  }
  public static MethodInfo GetMethodRecursive(this Type t, string methodName)
    => GetMethodRecursive(t, methodName, null);

  public static ConstructorInfo GetConstructor(
    Type resolvedType,
    Type[]? parameterTypes = null)
  {
    ConstructorInfo ctorInfo = default;

    var methods = resolvedType.GetConstructors((BindingFlags)0xffff);
    ConstructorInfo[] exactMatches = methods
      .Where(m =>
        m.GetParameters()
          .Select(pi => pi.ParameterType)
          .SequenceEqual(parameterTypes))
      .ToArray();
    if (exactMatches.Length == 1)
    {
      ctorInfo = exactMatches.First();
    }
    else
    {
      // Do a less strict search
      ctorInfo = methods.SingleOrDefault(m =>
        m.GetParameters()
          .Select(pi => pi.ParameterType)
          .SequenceEqual(parameterTypes,
              new TypeExt.WildCardEnabledTypesComparer()));
    }

    return ctorInfo;
  }

  /// <summary>
  /// Searches a type for a specific field. If not found searches its ancestors.
  /// </summary>
  /// <param name="t">TypeFullName to search</param>
  /// <param name="fieldName">Field name to search</param>
  public static FieldInfo GetFieldRecursive(this Type t, string fieldName)
  {
    var field = t.GetFields((BindingFlags)0xffff)
      .SingleOrDefault(fi => fi.Name == fieldName);
    if (field != null)
      return field;

    // Not found in this type...
    if (t == typeof(object))
      return null; // No more parents

    // Check parent (until `object`)
    return t.BaseType.GetFieldRecursive(fieldName);
  }

  public static bool IsPrimitiveEtcArray(this Type realType)
  {
    if (!realType.IsArray)
      return false;

    Type elementsType = realType.GetElementType();
    return elementsType.IsPrimitiveEtc();
  }

  public static bool IsPrimitiveEtc(this Type realType)
  {
    return realType.IsPrimitive ||
      realType == typeof(string) ||
      realType == typeof(decimal) ||
      realType == typeof(DateTime);
  }

  public static bool IsStringCoercible(this Type realType)
  {
    // TODO: Apply more comprehensive check to ensure that these types are
    //       present in the consumer AppDomain and have `ToString` and `Parse`
    //       methods.
    return realType == typeof(Guid);
  }

  public static Type GetType(this AppDomain domain, string typeFullName)
  {
    var assemblies = domain.GetAssemblies();
    foreach (Assembly assm in assemblies)
    {
      Type t = assm.GetType(typeFullName);
      if (t != null)
        return t;
    }
    return null;
  }
}
