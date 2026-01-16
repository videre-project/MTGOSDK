/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  // Endpoint-level field cache: (typeFullName, fieldName) → FieldInfo
  private static readonly ConcurrentDictionary<(string, string), FieldInfo> 
    s_fieldCache = new();

  private byte[] MakeGetFieldResponse()
  {
    Log.Debug("[Diver] Got /get_field request!");

    var request = DeserializeRequest<FieldGetRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    Type dumpedObjType;
    object results;

    if (request.ObjAddress == 0)
    {
      dumpedObjType = _runtime.ResolveType(request.TypeFullName);
      FieldInfo staticFieldInfo = dumpedObjType.GetField(request.FieldName);
      if (!staticFieldInfo.IsStatic)
        return QuickError("Trying to get field with a null target but the field was not a static one");

      results = staticFieldInfo.GetValue(null);
    }
    else
    {
      if (!_runtime.TryGetPinnedObject(request.ObjAddress, out object instance))
        return QuickError("Can't get field of an unpinned object");

      dumpedObjType = instance.GetType();

      // Endpoint-level cache: use type fullname + field name as key
      var cacheKey = (dumpedObjType.FullName, request.FieldName);
      if (!s_fieldCache.TryGetValue(cacheKey, out var fieldInfo))
      {
        fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
        if (fieldInfo != null)
          s_fieldCache[cacheKey] = fieldInfo;
      }
      
      if (fieldInfo == null)
      {
        Debugger.Launch();
        Log.Debug("[Diver] Failed to Resolved field :/");
        return QuickError("Couldn't find field in type.");
      }

      Log.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

      try
      {
        // Try direct execution first - most field accesses don't need UI thread
        results = fieldInfo.GetValue(instance);
      }
      catch (Exception ex) when (STAThread.RequiresSTAThread(ex) || 
                                 (ex.InnerException != null && STAThread.RequiresSTAThread(ex.InnerException)))
      {
        // Retry on STA/UI thread only for fields that actually need it
        Log.Debug($"[Diver] Retrying GetField on STA thread due to: {ex.InnerException?.Message ?? ex.Message}");
        try
        {
          results = STAThread.Execute(() => fieldInfo.GetValue(instance));
        }
        catch (Exception retryEx)
        {
          return QuickError($"Invocation caused exception (after STA retry): {retryEx}");
        }
      }
      catch (Exception e)
      {
        return QuickError($"Invocation caused exception: {e}");
      }
    }

    ObjectOrRemoteAddress returnValue;
    if (results.GetType().IsPrimitiveEtc())
    {
      returnValue = ObjectOrRemoteAddress.FromObj(results);
    }
    else
    {
      ulong resultsAddress = _runtime.PinObject(results);
      Type resultsType = results.GetType();
      int hashCode = results.GetHashCode();
      returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.FullName ?? resultsType.Name, hashCode);
    }

    var invocResults = new InvocationResults
    {
      VoidReturnType = false,
      ReturnedObjectOrAddress = returnValue
    };

    return WrapSuccess(invocResults);
  }
}
