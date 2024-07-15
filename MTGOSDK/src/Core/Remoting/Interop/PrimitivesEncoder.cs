/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection.Extensions;


namespace MTGOSDK.Core.Remoting.Interop;

public static class PrimitivesEncoder
{
  /// <summary>
  /// Encodes a primitive or array of primitives
  /// </summary>
  /// <param name="toEncode">Object or array to encode</param>
  /// <returns>Encoded value as a string</returns>
  public static string Encode(object toEncode)
  {
    if (toEncode == null) // This is specific for the String case, but I can't guarantee it here...
      return string.Empty;

    Type t = toEncode.GetType();
    if (t == typeof(string))
    {
      return $"\"{toEncode}\"";
    }

    if (t.IsPrimitiveEtc() || t.IsStringCoercible() || t.IsEnum)
    {
      // These types can just be ".Parse()"-ed back
      return toEncode.ToString();
    }

    if (toEncode is not Array enumerable)
    {
      throw new ArgumentException(
        $"Object to encode was not a primitive or an array. TypeFullName: {t}");
    }

    if (!t.IsPrimitiveEtcArray())
    {
      // TODO: Support arrays of RemoteObjects/DynamicRemoteObject
      throw new Exception("At least one element in the array is not primitive");
    }

    // Otherwise - this is an array of primitives.
    string output = string.Empty;
    object[] objectsEnumerable = enumerable.Cast<object>().ToArray();
    foreach (object o in objectsEnumerable)
    {
      string currObjectValue = Encode(o);
      // Escape commas
      currObjectValue = currObjectValue.Replace(",", "\\,");
      if (output != string.Empty)
      {
        output += ",";
      }

      output += $"\"{currObjectValue}\"";
    }

    return output;
  }

  public static bool TryEncode(object toEncode, out string res)
  {
    res = default;
    if (!(toEncode.GetType().IsPrimitiveEtc()))
    {
      // Not primitive ETC nor array --> not primitive
      if (!(toEncode is Array)) return false;

      Type elementsType = toEncode.GetType().GetElementType();
      // Array of non-primitives --> not primitive
      if (!elementsType.IsPrimitiveEtc()) return false;
    }

    // All good, can encode with no exceptions:
    res = Encode(toEncode);
    return true;
  }

  public static object Decode(ObjectOrRemoteAddress oora)
  {
    if (oora.IsRemoteAddress)
      throw new ArgumentException(
        "Can not decode ObjectOrRemoteAddress object which represents a remote address.");
    return Decode(oora.EncodedObject, oora.Type);
  }

  public static object Decode(string toDecode, Type resultType)
  {
    // Easiest case - strings are encoded to themselves
    if (resultType == typeof(string))
    {
      if (toDecode == "null")
        return null;
      else if (toDecode[0] == '"' && toDecode[toDecode.Length - 1] == '"')
        return toDecode.Substring(1, toDecode.Length - 2);
      else
        throw new Exception("Missing quotes on encoded string");
    }

    // If the type is a primitive or string coercible, we can parse it
    // directly from the string using the type's Parse method.
    if (resultType.IsPrimitiveEtc() || resultType.IsStringCoercible())
    {
      var parseMethod = resultType.GetMethod("Parse", [typeof(string)]);
      return parseMethod.Invoke(null, new object[] { toDecode });
    }

    // If the type is an enum, we can parse it directly from the string
    if (resultType.IsEnum)
    {
      return Enum.Parse(resultType, toDecode);
    }

    if (resultType.IsArray)
    {
      Type elementType = resultType.GetElementType();

      // Empty array
      if (string.IsNullOrEmpty(toDecode))
        return Array.CreateInstance(elementType, 0);

      // Capture the position of each element in the string
      List<int> commas = new();
      commas.Add(0); // To capture the first item we need to "imagine a comma" right before it.
      for (int i = 1; i < toDecode.Length; i++)
      {
        if (toDecode[i] == ',' && toDecode[i - 1] != '\\')
        {
          commas.Add(i);
        }
      }

      // Extract each element from the string
      List<string> encodedElements = new();
      for (int i = 0; i < commas.Count; i++)
      {
        int currCommaIndex = commas[i];
        int nextCommandIndex = toDecode.Length;
        if (i != commas.Count - 1)
        {
          nextCommandIndex = commas[i + 1];
        }
        encodedElements.Add(
          toDecode.Substring(currCommaIndex + 1,
                             nextCommandIndex - currCommaIndex - 1).Trim('\"'));
      }

      // Decode each extracted element
      List<object> decodedObjects = new();
      foreach (string encodedElement in encodedElements)
      {
        var unescapedEncElement = encodedElement.Replace("\\,", ",");
        object decodedObject = Decode(unescapedEncElement, elementType);
        decodedObjects.Add(decodedObject);
      }

      // Create a runtime array of the extracted elements with the correct type
      Array arr = Array.CreateInstance(elementType, decodedObjects.Count);
      for (int i = 0; i < decodedObjects.Count; i++)
      {
        arr.SetValue(decodedObjects[i], i);
      }

      return arr;
    }

    throw new ArgumentException(
      $"Result type was not a primitive or an array. TypeFullName: {resultType}");
  }

  public static object Decode(string toDecode, string fullTypeName)
  {
    // NOTE: I'm allowing this decode to be restricted to the current domain
    //       (Instead of searching in all domains) because I want to believe
    //       only primitive types will be handed here and ideally those should
    //       all be available in all domains.
    Type t = AppDomain.CurrentDomain.GetType(fullTypeName);
    if (t != null) return Decode(toDecode, t);

    throw new Exception(
      $"Could not resolve type name \"{fullTypeName}\" in the current AppDomain");
  }
}
