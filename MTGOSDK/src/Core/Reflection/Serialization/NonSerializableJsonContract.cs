/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

#if !MTGOSDKCORE
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using MTGOSDK.Core.Reflection.Attributes;


namespace MTGOSDK.Core.Reflection.Serialization;

public enum NonSerializableJsonPropertyAction
{
  Remove,
  Stringify,
  StringifyEnumerable,
}

public sealed class NonSerializableJsonPropertyDirective
{
  public NonSerializableJsonPropertyDirective(
    NonSerializableJsonPropertyAction action,
    IReadOnlyList<string> propertyNames)
  {
    Action = action;
    PropertyNames = propertyNames;
  }

  public NonSerializableJsonPropertyAction Action { get; }

  public IReadOnlyList<string> PropertyNames { get; }
}

/// <summary>
/// Describes the JSON property shape implied by <see cref="NonSerializableAttribute" />.
/// </summary>
public static class NonSerializableJsonContract
{
  public static IReadOnlyList<NonSerializableJsonPropertyDirective>
    GetPropertyDirectives(Type type)
  {
    ArgumentNullException.ThrowIfNull(type);

    return type
      .GetProperties(BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.Instance)
      .Select(CreateDirective)
      .Where(directive => directive != null)
      .Cast<NonSerializableJsonPropertyDirective>()
      .ToArray();
  }

  private static NonSerializableJsonPropertyDirective? CreateDirective(
    PropertyInfo property)
  {
    var propertyNames = GetJsonPropertyNames(property).Distinct().ToArray();
    if (property.GetCustomAttribute<NonSerializableAttribute>() != null)
    {
      return new NonSerializableJsonPropertyDirective(
        NonSerializableJsonPropertyAction.Remove,
        propertyNames);
    }

    var propertyTypeAttribute =
      property.PropertyType.GetCustomAttribute<NonSerializableAttribute>();
    if (propertyTypeAttribute != null)
    {
      return new NonSerializableJsonPropertyDirective(
        ShouldStringifyPropertyType(property.PropertyType)
          ? NonSerializableJsonPropertyAction.Stringify
          : NonSerializableJsonPropertyAction.Remove,
        propertyNames);
    }

    var elementType = GetEnumerableElementType(property.PropertyType);
    var elementTypeAttribute =
      elementType?.GetCustomAttribute<NonSerializableAttribute>();
    if (elementType == null || elementTypeAttribute == null)
    {
      return null;
    }

    return new NonSerializableJsonPropertyDirective(
      ShouldStringifyRecursiveType(elementType, elementTypeAttribute)
        ? NonSerializableJsonPropertyAction.StringifyEnumerable
        : NonSerializableJsonPropertyAction.Remove,
      propertyNames);
  }

  private static bool ShouldStringifyPropertyType(Type type) =>
    type.GetMethod(nameof(ToString))?.DeclaringType != typeof(object);

  private static bool ShouldStringifyRecursiveType(
    Type type,
    NonSerializableAttribute attribute) =>
      attribute.Behavior == SerializationBehavior.Stringify ||
      type.GetMethod(nameof(ToString))?.DeclaringType != typeof(object);

  private static Type? GetEnumerableElementType(Type type)
  {
    if (type == typeof(string))
    {
      return null;
    }

    if (type.IsArray)
    {
      return type.GetElementType();
    }

    if (type.IsGenericType &&
        type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
    {
      return type.GetGenericArguments()[0];
    }

    return type
      .GetInterfaces()
      .Where(interfaceType =>
        interfaceType.IsGenericType &&
        interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
      .Select(interfaceType => interfaceType.GetGenericArguments()[0])
      .FirstOrDefault();
  }

  private static IEnumerable<string> GetJsonPropertyNames(PropertyInfo property)
  {
    var jsonPropertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
    if (jsonPropertyName != null)
    {
      yield return jsonPropertyName.Name;
    }

    yield return property.Name;
    yield return JsonNamingPolicy.CamelCase.ConvertName(property.Name);
  }
}
#endif
