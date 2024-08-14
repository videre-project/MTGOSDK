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
  private string MakeGetFieldResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /get_field request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    TextReader textReader = new StringReader(body);
    FieldSetRequest request = JsonConvert.DeserializeObject<FieldSetRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    // Need to figure target instance and the target type.
    // In case of a static call the target instance stays null.
    Type dumpedObjType;
    object results;
    if (request.ObjAddress == 0)
    {
      // Null Target -- Getting a Static field
      dumpedObjType = _runtime.ResolveType(request.TypeFullName);
      FieldInfo staticFieldInfo = dumpedObjType.GetField(request.FieldName);
      if (!staticFieldInfo.IsStatic)
      {
        return QuickError("Trying to get field with a null target bu the field was not a static one");
      }

      results = staticFieldInfo.GetValue(null);
    }
    else
    {
      object instance;
      // Check if we have this objects in our pinned pool
      if (_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
      {
        // Found pinned object!
        dumpedObjType = instance.GetType();
      }
      else
      {
        return QuickError("Can't get field of a unpinned objects");
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

      try
      {
        results = fieldInfo.GetValue(instance);
      }
      catch (Exception e)
      {
        return QuickError($"Invocation caused exception: {e}");
      }
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
