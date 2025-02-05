/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

#if !MTGOSDKCORE
using System.Text.Json;
using System.Text.Json.Serialization;
#endif


namespace MTGOSDK.Core.Reflection.Serialization;

#if !MTGOSDKCORE
public class JsonSerializableEnumerableConverter : JsonConverterFactory
{
  public override bool CanConvert(Type typeToConvert) =>
    typeToConvert.IsGenericType &&
    typeToConvert.GetGenericArguments().Length == 1 &&
    typeof(IEnumerable<IJsonSerializable>).IsAssignableFrom(typeToConvert);

  public override JsonConverter CreateConverter(
    Type typeToConvert,
    JsonSerializerOptions options) =>
      (JsonConverter)Activator.CreateInstance(
        typeof(JsonSerializableEnumerableConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
}

public class JsonSerializableEnumerableConverter<T> : JsonConverter<IEnumerable<T>>
    where T : IJsonSerializable
{
  public override IEnumerable<T> Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options) => default;

  public override void Write(
    Utf8JsonWriter writer,
    IEnumerable<T> value,
    JsonSerializerOptions options)
  {
    writer.WriteStartArray();
    foreach (var item in value)
    {
      JsonSerializer.Serialize(writer, item.SerializeRecursive(), options);
    }
    writer.WriteEndArray();
  }
}
#endif
