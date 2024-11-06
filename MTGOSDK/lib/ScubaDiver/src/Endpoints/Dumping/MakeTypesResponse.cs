/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;

using Newtonsoft.Json;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeTypesResponse(HttpListenerRequest req)
  {
    string assembly = req.QueryString.Get("assembly");
    Assembly matchingAssembly = _runtime.ResolveAssembly(assembly);
    if (matchingAssembly == null)
      return QuickError($"No assemblies found matching the query '{assembly}'");

    List<TypesDump.TypeIdentifiers> types = new List<TypesDump.TypeIdentifiers>();
    foreach (Type type in matchingAssembly.GetTypes())
    {
      types.Add(new TypesDump.TypeIdentifiers() { TypeName = type.FullName });
    }

    TypesDump dump = new() { AssemblyName = assembly, Types = types };
    return JsonConvert.SerializeObject(dump);
  }
}
