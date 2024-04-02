/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;


namespace MTGOSDK.Core.Reflection.Snapshot;

public static class ClrExt
{
  public struct TypeDefToMethod
  {
    public ulong MethodTable { get; set; }
    public int Token { get; set; }
  }

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

  public static byte[] ToByteArray(this ClrObject obj)
  {
    return obj.AsArray().ToByteArray();
  }

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
