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
  private string MakeCreateObjectResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /create_object request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    var request = JsonConvert.DeserializeObject<CtorInvocationRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }


    Type t = _runtime.ResolveType(request.TypeFullName);
    if (t == null)
    {
      return QuickError("Failed to resolve type");
    }

    List<object> paramsList = new();
    if (request.Parameters.Any())
    {
      Log.Debug($"[Diver] Ctor'ing with parameters. Count: {request.Parameters.Count}");
      paramsList = request.Parameters.Select(_runtime.ParseParameterObject).ToList();
    }
    else
    {
      // No parameters.
      Log.Debug("[Diver] Ctor'ing without parameters");
    }

    object createdObject = null;
    try
    {
      object[] paramsArray = paramsList.ToArray();
      createdObject = Activator.CreateInstance(t, paramsArray);
    }
    catch
    {
      Debugger.Launch();
      return QuickError("Activator.CreateInstance threw an exception");
    }

    if (createdObject == null)
    {
      return QuickError("Activator.CreateInstance returned null");
    }

    // Need to return the results. If it's primitive we'll encode it
    // If it's non-primitive we pin it and send the address.
    ObjectOrRemoteAddress res;
    ulong pinAddr;
    if (createdObject.GetType().IsPrimitiveEtc())
    {
      // TODO: Something else?
      pinAddr = 0xeeffeeff;
      res = ObjectOrRemoteAddress.FromObj(createdObject);
    }
    else
    {
      // Pinning results
      pinAddr = _runtime.PinObject(createdObject);
      res = ObjectOrRemoteAddress.FromToken(pinAddr, createdObject.GetType().FullName);
    }

    InvocationResults invoRes = new()
    {
      ReturnedObjectOrAddress = res,
      VoidReturnType = false
    };

    return JsonConvert.SerializeObject(invoRes);
  }
}
