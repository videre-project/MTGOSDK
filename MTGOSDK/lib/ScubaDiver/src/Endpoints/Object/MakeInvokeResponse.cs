/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;

using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeInvokeResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /Invoke request!");
    string body = ReadRequestBody(arg);

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    TextReader textReader = new StringReader(body);
    var request = JsonConvert.DeserializeObject<InvocationRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    // Need to figure target instance and the target type.
    // In case of a static call the target instance stays null.
    object instance = null;
    Type dumpedObjType;
    if (request.ObjAddress == 0)
    {
      //
      // Null target - static call
      //

      dumpedObjType = _runtime.ResolveType(request.TypeFullName);
    }
    else
    {
      //
      // Non-null target object address. Non-static call
      //

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
            return QuickError("'address' points at an invalid address");
          }

          // Make sure it's still in place
          _runtime.RefreshRuntime();
          clrObj = _runtime.GetClrObject(request.ObjAddress);
        }
        if (clrObj.Type == null)
        {
          return
            QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
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
            QuickError("Couldn't get handle to requested object. It could be because the Method Table mismatched or a GC collection happened.");
        }
      }
    }

    //
    // We have our target and it's type. No look for a matching overload for the
    // function to invoke.
    //
    List<object> paramsList = new();
    if (request.Parameters.Any())
    {
      Log.Debug($"[Diver] Invoking with parameters. Count: {request.Parameters.Count}");
      paramsList = request.Parameters.Select(_runtime.ParseParameterObject).ToList();
    }
    else
    {
      // No parameters.
      Log.Debug("[Diver] Invoking without parameters");
    }

    // Infer parameter types from received parameters.
    // Note that for 'null' arguments we don't know the type so we use a "Wild Card" type
    Type[] argumentTypes = paramsList.Select(p => p?.GetType() ?? new TypeStub()).ToArray();

    // Get types of generic arguments <T1,T2, ...>
    Type[] genericArgumentTypes = request.GenericArgsTypeFullNames.Select(typeFullName => _runtime.ResolveType(typeFullName)).ToArray();

    // Search the method with the matching signature
    var method = dumpedObjType.GetMethodRecursive(request.MethodName, genericArgumentTypes, argumentTypes);
    if (method == null)
    {
      Debugger.Launch();
      Log.Debug($"[Diver] Failed to Resolved method :/");
      return QuickError("Couldn't find method in type.");
    }

    string argsSummary = string.Join(", ", argumentTypes.Select(arg => arg.Name));
    Log.Debug($"[Diver] Resolved method: {method.Name}({argsSummary}), Containing Type: {method.DeclaringType}");

    object results = null;
    string[] suppressTypes = ["SecureString"];
    try
    {
      argsSummary = string.Join(", ", paramsList.Select(param => param?.ToString() ?? "null"));
      if (string.IsNullOrEmpty(argsSummary))
        argsSummary = "No Arguments";
      else if (suppressTypes.Contains(method.DeclaringType.Name))
        argsSummary = "*";

      Log.Debug($"[Diver] Invoking {method.Name} with those args (Count: {paramsList.Count}): `{argsSummary}`");
      results = method.Invoke(instance, paramsList.ToArray());
    }
    catch (Exception e) when (STAThread.RequiresSTAThread(e))
    {
      // Re-throw STA-related exceptions so the dispatcher can retry on STA thread
      throw;
    }
    catch (Exception e)
    {
      // Unwrap TargetInvocationException to get the real error
      var innerEx = e;
      while (innerEx.InnerException != null)
        innerEx = innerEx.InnerException;

      Log.Debug($"[Diver] Invocation of {method.Name} failed: {innerEx.Message}");
      return QuickError($"Invocation caused exception: {innerEx.Message}", e.ToString());
    }

    InvocationResults invocResults;
    if (method.ReturnType == typeof(void))
    {
      // Not expecting results.
      invocResults = new InvocationResults() { VoidReturnType = true };
    }
    else
    {
      if (results == null)
      {
        // Got back a null...
        invocResults = new InvocationResults()
        {
          VoidReturnType = false,
          ReturnedObjectOrAddress = ObjectOrRemoteAddress.Null
        };
      }
      else
      {
        // Need to return the results. If it's primitive we'll encode it
        // If it's non-primitive we pin it and send the address.
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
    }
    return JsonConvert.SerializeObject(invocResults);
  }
}
