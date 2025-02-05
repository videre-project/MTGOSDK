/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Dynamic;
using System.Reflection;

#if !MTGOSDKCORE
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.Core.Reflection.Serialization;

public static class JsonSerializableExtensions
{
#if !MTGOSDKCORE
  private static readonly JsonSerializerOptions s_serializerOptions = new()
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    IncludeFields = false,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = true,
  };

  private static dynamic SerializeRecursive(
    object obj,
    PropertyInfo? property,
    JsonSerializerOptions options = null)
  {
    Type type = property.PropertyType;
    if (type.GetCustomAttribute<NonSerializableAttribute>() != null &&
        type.GetMethod("ToString").DeclaringType == typeof(object))
    {
      return null;
    }

    var value = Retry(() => property.GetValue(obj));
    if (value == null)
      return type.IsValueType ? Activator.CreateInstance(type) : null;

    return SerializeRecursive(value, type, options);
  }

  public static dynamic SerializeRecursive(
    this object obj,
    JsonSerializerOptions options = null,
    bool nonSerializable = false) =>
      SerializeRecursive(obj, obj.GetType(), options, nonSerializable);

  private static dynamic SerializeRecursive(
    object value,
    Type type,
    JsonSerializerOptions options = null,
    bool nonSerializable = false)
  {
    var attr = type.GetCustomAttribute<NonSerializableAttribute>();
    nonSerializable = nonSerializable || attr != null;
    if (nonSerializable)
    {
      if ((attr != null && attr.Behavior == SerializationBehavior.Stringify) ||
          type.GetMethod("ToString").DeclaringType != typeof(object))
      {
        return value.ToString();
      }
      return null;
    }

    if (type.IsEnum)
    {
      return value.ToString();
    }
    else if (value is IJsonSerializable jsonBase)
    {
      return Serialize(jsonBase, options, true);
    }
    else if (value is IEnumerable<IJsonSerializable> enumerable)
    {
      return enumerable.Select(e =>
        SerializeRecursive(e, options, nonSerializable));
    }
    else if (value is IDictionary dict && dict.Count > 0)
    {
      // Serialize the keys and values of the dictionary separately.
      var keys = dict.Keys.Cast<object>().Select(k =>
        SerializeRecursive(k, options, nonSerializable));
      var values = dict.Values.Cast<object>().Select(v =>
        SerializeRecursive(v, options, nonSerializable));

      // Create a new dict of the serialized keys and values' types.
      var dictType = typeof(Dictionary<,>).MakeGenericType(
        keys.First().GetType(),
        values.First().GetType());

      var newDict = (IDictionary)Activator.CreateInstance(dictType);
      foreach (var (key, val) in keys.Zip(values))
      {
        newDict.Add(key, val);
      }
      return newDict;
    }
    else
    {
      return value;
    }
  }

  public static dynamic Serialize(
    IJsonSerializable obj,
    JsonSerializerOptions options,
    bool isRecursive = false)
  {
    // Get all properties of the object, public and non-public.
    var properties = obj.GetType().GetProperties(
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    // Only include public properties or those with a JsonInclude attribute;
    // exclude all properties with a JsonIgnore attribute.
    var filteredProperties = properties
      .Where(p => p.GetCustomAttribute<NonSerializableAttribute>() == null &&
                  p.GetGetMethod()?.IsPublic == true)
      .OrderBy(p => p.MetadataToken)
      .ToList();

    // Create an ExpandoObject to store the serialized object.
    var expando = new ExpandoObject();
    var expandoDict = (IDictionary<string, object>)expando;
    expandoDict["$type"] = obj.GetType().Name;

    // Serialize each property of the object.
    foreach (var property in filteredProperties)
    {
      expandoDict[property.Name] = SerializeRecursive(obj, property, options);
    }
    // If the method was called recursively, defer serialization to the caller.
    if (isRecursive) return expando;

    // Serialize the ExpandoObject to a JSON string.
    return JsonSerializer.Serialize(expando, options);
  }

  public static string ToJSON(this IJsonSerializable obj) =>
    Serialize(obj, s_serializerOptions);

  public static dynamic ToSerializable(this IJsonSerializable obj) =>
    Serialize(obj, s_serializerOptions, true);
#endif
}
