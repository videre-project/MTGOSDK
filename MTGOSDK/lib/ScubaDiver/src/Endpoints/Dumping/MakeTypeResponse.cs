/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Net;
using System.Text.Json;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeTypeResponse(HttpListenerRequest req)
  {
    string body = null;
    using (StreamReader sr = new(req.InputStream))
    {
      body = sr.ReadToEnd();
    }
    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    var request = JsonSerializer.Deserialize<TypeDumpRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    return MakeTypeResponse(request);
  }

  public string MakeTypeResponse(TypeDumpRequest dumpRequest)
  {
    string type = dumpRequest.TypeFullName;
    if (string.IsNullOrEmpty(type))
    {
      return QuickError("Missing parameter 'TypeFullName'");
    }

    string assembly = dumpRequest.Assembly;
    //Log.Debug($"[Diver] Trying to dump Type: {type}");
    if (assembly != null)
    {
      //Log.Debug($"[Diver] Trying to dump Type: {type}, WITH Assembly: {assembly}");
    }
    Type resolvedType = _runtime.ResolveType(type, assembly);

    if (resolvedType != null)
    {
      TypeDump recursiveTypeDump = TypeDump.ParseType(resolvedType);
      return JsonSerializer.Serialize(recursiveTypeDump);
    }

    return QuickError("Failed to find type in searched assemblies");
  }
}
