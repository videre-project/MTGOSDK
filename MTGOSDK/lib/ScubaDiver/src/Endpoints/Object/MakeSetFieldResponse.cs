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
  private string MakeSetFieldResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /set_field request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    var request = JsonConvert.DeserializeObject<FieldSetRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    Type dumpedObjType;
    if (request.ObjAddress == 0)
    {
      return QuickError("Can't set field of a null target");
    }


    // Need to figure target instance and the target type.
    // In case of a static call the target instance stays null.
    object instance;
    // Check if we have this objects in our pinned pool
    if (_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
    {
      // Found pinned object!
      dumpedObjType = instance.GetType();
    }
    else
    {
      // Object not pinned, try get it the hard way
      ClrObject clrObj = default;
      lock (_runtime.clrLock)
      {
        clrObj = _runtime.GetClrObject(request.ObjAddress);
        if (clrObj.Type == null)
        {
          return QuickError($"The invalid address for '${request.TypeFullName}'.");
        }

        // Make sure it's still in place
        _runtime.RefreshRuntime();
        clrObj = _runtime.GetClrObject(request.ObjAddress);
      }
      if (clrObj.Type == null)
      {
        return
          QuickError($"The address for '${request.TypeFullName}' moved since last refresh.");
      }

      ulong mt = clrObj.Type.MethodTable;
      dumpedObjType = _runtime.ResolveType(clrObj.Type.Name);
      try
      {
        instance = _runtime.Compile(clrObj.Address, mt);
      }
      catch (Exception)
      {
        return
          QuickError("Couldn't get handle to requested object. It could be because the Method Table or a GC collection happened.");
      }
    }

    // Search the method with the matching signature
    var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
    if (fieldInfo == null)
    {
      Debugger.Launch();
      Log.Debug($"[Diver] Failed to Resolved field :/");
      return QuickError("Couldn't find field in type.");
    }
    Log.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

    object results = null;
    try
    {
      object value = _runtime.ParseParameterObject(request.Value);
      fieldInfo.SetValue(instance, value);
      // Reading back value to return to caller. This is expected C# behaviour:
      // int x = this.field_y = 3; // Makes both x and field_y equal 3.
      results = fieldInfo.GetValue(instance);
    }
    catch (Exception e)
    {
      return QuickError($"Invocation caused exception: {e}");
    }


    // Return the value we just set to the field to the caller...
    InvocationResults invocResults;
    {
      ObjectOrRemoteAddress returnValue;
      if (results.GetType().IsPrimitiveEtc())
      {
        returnValue = ObjectOrRemoteAddress.FromObj(results);
      }
      else
      {
        // Pinning results
        ulong resultsAddress = _runtime.PinObject(results);
        Type resultsType = results.GetType();
        returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
      }

      invocResults = new InvocationResults()
      {
        VoidReturnType = false,
        ReturnedObjectOrAddress = returnValue
      };
    }
    return JsonConvert.SerializeObject(invocResults);
  }
}
