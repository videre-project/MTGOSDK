/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

#if !MTGOSDKCORE
using System.Text.Json;
using System.Text.Json.Serialization;
#endif


namespace MTGOSDK.Core.Reflection.Serialization;

#if !MTGOSDKCORE
public class JsonSerializableConverter<T> : JsonConverter<T>
    where T : JsonSerializableBase
{
  public override T Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options) => default;

  public override void Write(
    Utf8JsonWriter writer,
    T value,
    JsonSerializerOptions options) =>
      JsonSerializer.Serialize(writer, value.ToSerializable(), options);
}

public class JsonSerializableConverter : JsonConverterFactory
{
  public override bool CanConvert(Type typeToConvert) =>
    typeof(JsonSerializableBase).IsAssignableFrom(typeToConvert);

  public override JsonConverter CreateConverter(
    Type typeToConvert,
    JsonSerializerOptions options) =>
      (JsonConverter)Activator.CreateInstance(
        typeof(JsonSerializableConverter<>).MakeGenericType(typeToConvert));
}
#endif
