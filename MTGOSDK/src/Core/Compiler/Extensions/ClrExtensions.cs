/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Core.Compiler.Structs;


namespace MTGOSDK.Core.Compiler.Extensions;

public static class ClrExtensions
{
  /// <summary>
  /// Converts a ClrArray to a raw byte array.
  /// </summary>
  /// <param name="arr">The ClrArray to convert.</param>
  /// <returns>The byte array.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the ClrArray is not a byte array.
  /// </exception>
  public static byte[] ToByteArray(this ClrArray arr)
  {
    try
    {
      arr.GetValue<byte>(0);
    }
    catch (Exception ex)
    {
      throw new ArgumentException("Not a byte array", ex);
    }

    byte[] res = new byte[arr.Length];
    for (int i = 0; i < res.Length; i++)
    {
      res[i] = arr.GetValue<byte>(i);
    }

    return res;
  }

  /// <summary>
  /// Converts a ClrObject to a raw byte array.
  /// </summary>
  /// <param name="obj">The ClrObject to convert.</param>
  /// <returns>The byte array.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the ClrObject is not an array.
  /// </exception>
  public static byte[] ToByteArray(this ClrObject obj)
  {
    return obj.AsArray().ToByteArray();
  }

  /// <summary>
  /// Enumerates the TypeDefToMethodTableMap for a ClrModule object.
  /// </summary>
  /// <param name="mod">The ClrModule object.</param>
  /// <returns>An IEnumerable of TypeDefToMethod objects.</returns>
  public static IEnumerable<TypeDefToMethod> EnumerateTypeDefToMethodTableMap(this ClrModule mod)
  {
    // EnumerateTypeDefToMethodTableMap wants to return an IEnumerable<(ulong,int)>
    // to us but returning tuples costs us another dependency so we're avoiding it.
    IEnumerable unresolvedEnumerable = typeof(ClrModule)
      .GetMethod("EnumerateTypeDefToMethodTableMap")
      .Invoke(mod, new object[0]) as IEnumerable;

    foreach (object o in unresolvedEnumerable)
    {
      var type = o.GetType();
      ulong mt = (ulong)type.GetField("Item1").GetValue(o);
      int token = (int)type.GetField("Item2").GetValue(o);

      yield return new TypeDefToMethod() { MethodTable = mt, Token = token };
    }
  }
}
