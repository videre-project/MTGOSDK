/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Dynamic;
using System.Reflection;

#if !MTGOSDKCORE
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.Core.Reflection.Serialization;

public abstract class JsonSerializableBase
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
    object value,
    JsonSerializerOptions options = null) =>
      SerializeRecursive(value, value.GetType(), options);

  private static dynamic SerializeRecursive(
    object value,
    Type type,
    JsonSerializerOptions options = null)
  {
    if (type.GetCustomAttribute<NonSerializableAttribute>() != null)
    {
      if (type.GetMethod("ToString").DeclaringType != typeof(object))
      {
        return value.ToString();
      }
      return null;
    }

    if (type.IsEnum)
    {
      return value.ToString();
    }
    else if (value is JsonSerializableBase jsonBase)
    {
      return Serialize(jsonBase, options, true);
    }
    // Check if the property is IEnumerable of JsonSerializableBase objects.
    else if (value is IEnumerable<JsonSerializableBase> enumerable)
    {
      return enumerable.Select(item => SerializeRecursive(item, options));
    }
    else
    {
      return value;
    }
  }

  public static dynamic Serialize(
    JsonSerializableBase obj,
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
      Type type = property.PropertyType;
      // var value = property.GetValue(obj);
      // expandoDict[property.Name] = SerializeRecursive(value, type, options);

      var value = Retry(() => property.GetValue(obj));
      expandoDict[property.Name] = value == null
        ? (type.IsValueType ? Activator.CreateInstance(type) : null)
        : SerializeRecursive(value, type, options);
    }
    // If the method was called recursively, defer serialization to the caller.
    if (isRecursive) return expando;

    // Serialize the ExpandoObject to a JSON string.
    return JsonSerializer.Serialize(expando, options);
  }

  public string ToJSON() => Serialize(this, s_serializerOptions);

  public dynamic ToSerializable() => Serialize(this, s_serializerOptions, true);
#endif
}
