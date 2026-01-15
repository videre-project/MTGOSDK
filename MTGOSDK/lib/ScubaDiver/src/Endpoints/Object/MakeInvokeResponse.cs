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
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private static readonly TypeStub s_typeStub = new();

  // Endpoint-level method cache: uses string keys for safety
  // Key: (typeFullName, methodName, paramCount, paramTypeHash)
  private static readonly ConcurrentDictionary<(string, string, int, int), MethodInfo> 
    s_methodCache = new();

  private static int ComputeTypeHash(Type[] types)
  {
    if (types == null) return 0;
    int hash = 17;
    foreach (var t in types)
      hash = hash * 31 + (t?.FullName?.GetHashCode() ?? 0);
    return hash;
  }

  private byte[] MakeInvokeResponse()
  {
    var request = DeserializeRequest<InvocationRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    Log.Debug($"[Diver] Got /Invoke request: method={request.MethodName}, type={request.TypeFullName}, addr={request.ObjAddress:X}");

    object instance = null;
    Type dumpedObjType;
    if (request.ObjAddress == 0)
    {
      dumpedObjType = _runtime.ResolveType(request.TypeFullName);
    }
    else
    {
      if (!_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
        return QuickError("Couldn't find object in pinned pool");

      dumpedObjType = instance.GetType();
    }

    int paramCount = request.Parameters?.Count ?? 0;
    object[] paramsArray = new object[paramCount];
    Type[] argumentTypes = new Type[paramCount];

    for (int i = 0; i < paramCount; i++)
    {
      paramsArray[i] = _runtime.ParseParameterObject(request.Parameters[i]);
      argumentTypes[i] = paramsArray[i]?.GetType() ?? typeof(object);
    }

    // Create cache key
    var cacheKey = (dumpedObjType.FullName, request.MethodName, paramCount, ComputeTypeHash(argumentTypes));

    // Try cache first
    if (!s_methodCache.TryGetValue(cacheKey, out var method))
    {
      // Use existing reflection helper
      method = dumpedObjType.GetMethodRecursive(
        request.MethodName,
        argumentTypes
      );

      if (method != null)
        s_methodCache[cacheKey] = method;
    }

    if (method == null)
      return QuickError($"Couldn't find method '{request.MethodName}' on type '{dumpedObjType.FullName}'");

    // Handle generics
    if (request.GenericArgsTypeFullNames?.Length > 0)
    {
      Type[] genericArgs = new Type[request.GenericArgsTypeFullNames.Length];
      for (int i = 0; i < request.GenericArgsTypeFullNames.Length; i++)
      {
        genericArgs[i] = _runtime.ResolveType(request.GenericArgsTypeFullNames[i]);
        if (genericArgs[i] == null)
          return QuickError($"Failed to resolve generic type: {request.GenericArgsTypeFullNames[i]}");
      }
      method = method.MakeGenericMethod(genericArgs);
    }
    // Check if this invocation requires UI thread affinity
    // DispatcherObject instances must be accessed on their owning thread
    bool isDispatcherObject = instance != null && instance is System.Windows.Threading.DispatcherObject;
    bool needsUIThread = request.ForceUIThread && isDispatcherObject;

    // Start sub-activity for the actual reflection invocation
    using var activity = s_activitySource.StartActivity("MethodInvoke");
    if (activity != null)
    {
      activity.SetTag("method", request.MethodName);
      activity.SetTag("type", request.TypeFullName);
      activity.SetTag("addr", request.ObjAddress.ToString("X"));
    }

    object results;
    try
    {
      if (needsUIThread && !STAThread.IsDispatcherThread)
      {
        // For DispatcherObject instances with ForceUIThread, invoke on UI thread proactively
        Log.Debug($"[Diver] Invoking {request.MethodName} on UI thread (DispatcherObject target)");
        results = STAThread.Execute(() => method.Invoke(instance, paramsArray));
      }
      else
      {
        // Try direct execution first - most operations don't need UI thread
        results = method.Invoke(instance, paramsArray);
      }
    }
    catch (Exception ex) when (STAThread.RequiresSTAThread(ex) || 
                               (ex.InnerException != null && STAThread.RequiresSTAThread(ex.InnerException)))
    {
      // Retry on STA/UI thread only for operations that actually need it
      Log.Debug($"[Diver] Retrying Invoke on STA thread due to: {ex.InnerException?.Message ?? ex.Message}");
      activity?.AddEvent(new ActivityEvent("STA_Retry"));
      try
      {
        results = STAThread.Execute(() => method.Invoke(instance, paramsArray));
      }
      catch (Exception retryEx)
      {
        activity?.SetStatus(ActivityStatusCode.Error, retryEx.Message);
        return QuickError($"Invocation caused exception (after STA retry): {retryEx}");
      }
    }
    catch (Exception e)
    {
      activity?.SetStatus(ActivityStatusCode.Error, e.Message);
      return QuickError($"Invocation caused exception: {e}");
    }

    ObjectOrRemoteAddress returnValue;
    if (method.ReturnType == typeof(void))
    {
      returnValue = ObjectOrRemoteAddress.Null;
    }
    else if (results == null)
    {
      returnValue = ObjectOrRemoteAddress.Null;
    }
    else if (results.GetType().IsPrimitiveEtc())
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
      VoidReturnType = method.ReturnType == typeof(void),
      ReturnedObjectOrAddress = returnValue
    };

    return WrapSuccess(invocResults);
  }
}
