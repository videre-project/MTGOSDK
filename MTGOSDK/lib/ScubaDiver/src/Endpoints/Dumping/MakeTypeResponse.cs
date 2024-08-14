/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Compiler.Snapshot;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


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

    TextReader textReader = new StringReader(body);
    var request = JsonConvert.DeserializeObject<TypeDumpRequest>(body);
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
      return JsonConvert.SerializeObject(recursiveTypeDump);
    }

    return QuickError("Failed to find type in searched assemblies");
  }
}
