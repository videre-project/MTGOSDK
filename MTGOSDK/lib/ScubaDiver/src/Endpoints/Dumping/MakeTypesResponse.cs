/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Reflection;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeTypesResponse()
  {
    var request = DeserializeRequest<TypesDumpRequest>();
    if (request == null || string.IsNullOrEmpty(request.Assembly))
      return QuickError("Missing or invalid 'Assembly' parameter");

    string assembly = request.Assembly;
    Assembly matchingAssembly = _runtime.ResolveAssembly(assembly);
    if (matchingAssembly == null)
      return QuickError($"No assemblies found matching the query '{assembly}'");

    var types = new List<TypesDump.TypeIdentifiers>();
    try
    {
      foreach (Type type in matchingAssembly.GetTypes())
      {
        if (type == null) continue;
        types.Add(new TypesDump.TypeIdentifiers { TypeName = type.FullName });
      }
    }
    catch (ReflectionTypeLoadException rtle)
    {
      var loaderMsgs = new List<string>();
      if (rtle.LoaderExceptions != null)
      {
        foreach (var le in rtle.LoaderExceptions)
        {
          if (le == null) continue;
          try { loaderMsgs.Add(le.Message); }
          catch { loaderMsgs.Add(le.ToString()); }
        }
      }
      string details = loaderMsgs.Count > 0 ? string.Join("; ", loaderMsgs) : rtle.Message;
      return QuickError($"Failed to load types from assembly '{assembly}': {details}");
    }
    catch (Exception ex)
    {
      return QuickError($"Failed to load types from assembly '{assembly}': {ex.Message}");
    }

    var dump = new TypesDump { AssemblyName = assembly, Types = types };
    return WrapSuccess(dump);
  }
}
