/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;


namespace MTGOSDK.Core.Reflection.Serialization;

/// <summary>
/// Builds interface proxy objects from property dictionaries.
/// </summary>
public static class InterfaceProxyBuilder
{
  private static readonly ModuleBuilder s_moduleBuilder;
  private static readonly Dictionary<Type, Type> s_proxyTypeCache = new();
  private static readonly object s_lock = new();

  static InterfaceProxyBuilder()
  {
    var assemblyName = new AssemblyName("DynamicInterfaceProxies");
    var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
      assemblyName, AssemblyBuilderAccess.Run);
    s_moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
  }

  /// <summary>
  /// Creates an object implementing TInterface with property values from the dictionary.
  /// </summary>
  public static TInterface Create<TInterface>(IDictionary<string, object?> propertyValues)
    where TInterface : class
  {
    var proxyType = GetOrCreateProxyType<TInterface>();
    var instance = Activator.CreateInstance(proxyType);
    
    // Set property values
    foreach (var kvp in propertyValues)
    {
      // Handle nested paths (e.g., "Rarity.Name" -> "Rarity")
      var propName = kvp.Key.Contains('.') ? kvp.Key.Split('.')[0] : kvp.Key;
      
      var prop = proxyType.GetProperty(propName);
      if (prop != null && prop.CanWrite)
      {
        try
        {
          var value = ConvertValue(kvp.Value, prop.PropertyType);
          prop.SetValue(instance, value);
        }
        catch
        {
          // Ignore conversion failures
        }
      }
    }

    return (TInterface)instance!;
  }

  private static Type GetOrCreateProxyType<TInterface>()
  {
    var interfaceType = typeof(TInterface);
    
    lock (s_lock)
    {
      if (s_proxyTypeCache.TryGetValue(interfaceType, out var cached))
        return cached;

      var proxyType = CreateProxyType(interfaceType);
      s_proxyTypeCache[interfaceType] = proxyType;
      return proxyType;
    }
  }

  private static Type CreateProxyType(Type interfaceType)
  {
    var typeName = $"Proxy_{interfaceType.Name}_{Guid.NewGuid():N}";
    var typeBuilder = s_moduleBuilder.DefineType(
      typeName,
      TypeAttributes.Public | TypeAttributes.Class,
      typeof(object),
      new[] { interfaceType });

    // Implement each interface property
    foreach (var prop in interfaceType.GetProperties())
    {
      ImplementProperty(typeBuilder, prop);
    }

    return typeBuilder.CreateType()!;
  }

  private static void ImplementProperty(TypeBuilder typeBuilder, PropertyInfo interfaceProp)
  {
    var fieldName = $"__{interfaceProp.Name}";
    var fieldBuilder = typeBuilder.DefineField(
      fieldName,
      interfaceProp.PropertyType,
      FieldAttributes.Private);

    var propBuilder = typeBuilder.DefineProperty(
      interfaceProp.Name,
      PropertyAttributes.None,
      interfaceProp.PropertyType,
      null);

    // Getter
    var getMethod = typeBuilder.DefineMethod(
      $"get_{interfaceProp.Name}",
      MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName,
      interfaceProp.PropertyType,
      Type.EmptyTypes);

    var getIL = getMethod.GetILGenerator();
    getIL.Emit(OpCodes.Ldarg_0);
    getIL.Emit(OpCodes.Ldfld, fieldBuilder);
    getIL.Emit(OpCodes.Ret);

    propBuilder.SetGetMethod(getMethod);

    // Setter
    var setMethod = typeBuilder.DefineMethod(
      $"set_{interfaceProp.Name}",
      MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName,
      null,
      new[] { interfaceProp.PropertyType });

    var setIL = setMethod.GetILGenerator();
    setIL.Emit(OpCodes.Ldarg_0);
    setIL.Emit(OpCodes.Ldarg_1);
    setIL.Emit(OpCodes.Stfld, fieldBuilder);
    setIL.Emit(OpCodes.Ret);

    propBuilder.SetSetMethod(setMethod);
  }

  private static object? ConvertValue(object? value, Type targetType)
  {
    if (value == null)
      return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

    var valueType = value.GetType();
    
    if (targetType.IsAssignableFrom(valueType))
      return value;

    // Handle IList<string> from List<string>
    if (targetType.IsGenericType && 
        targetType.GetGenericTypeDefinition() == typeof(IList<>) &&
        value is System.Collections.IList list)
    {
      return value;
    }

    // Handle enum conversion from string
    if (targetType.IsEnum && value is string stringValue)
    {
      try
      {
        return Enum.Parse(targetType, stringValue, ignoreCase: true);
      }
      catch
      {
        // Return default enum value if parsing fails
        return Activator.CreateInstance(targetType);
      }
    }

    // Try conversion
    try
    {
      return Convert.ChangeType(value, targetType);
    }
    catch
    {
      return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
    }
  }
}
