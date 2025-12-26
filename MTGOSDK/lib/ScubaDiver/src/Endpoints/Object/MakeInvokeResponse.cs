/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
using System;
using System.Diagnostics;
using System.Net;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private static readonly TypeStub s_typeStub = new();

  private byte[] MakeInvokeResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /Invoke request!");
    var body = ReadRequestBody(arg);

    if (body == null || body.Length == 0)
      return QuickError("Missing body");

    InvocationRequest request;
    try
    {
      request = MessagePackSerializer.Deserialize<InvocationRequest>(body);
    }
    catch
    {
      return QuickError("Failed to deserialize body");
    }

    if (request == null)
      return QuickError("Failed to deserialize body");

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
      var parsed = _runtime.ParseParameterObject(request.Parameters[i]);
      paramsArray[i] = parsed;
      argumentTypes[i] = parsed?.GetType() ?? s_typeStub;
    }

    int genericCount = request.GenericArgsTypeFullNames?.Length ?? 0;
    Type[] genericArgumentTypes = new Type[genericCount];
    for (int i = 0; i < genericCount; i++)
    {
      genericArgumentTypes[i] = _runtime.ResolveType(request.GenericArgsTypeFullNames[i]);
    }

    var method = dumpedObjType.GetMethodRecursive(request.MethodName, genericArgumentTypes, argumentTypes);
    if (method == null)
    {
      Debugger.Launch();
      Log.Debug("[Diver] Failed to Resolved method :/");
      return QuickError("Couldn't find method in type.");
    }

    Log.Debug($"[Diver] Resolved method: {method.Name}, Containing Type: {method.DeclaringType}");

    // Handle generic method definitions that weren't properly constructed
    // This can happen if the method was found via recursion or wildcard matching
    if (method.IsGenericMethodDefinition)
    {
      if (genericArgumentTypes == null || genericArgumentTypes.Length == 0)
      {
        return QuickError($"Method '{method.Name}' is generic and requires type arguments, but none were provided.");
      }
      if (method.GetGenericArguments().Length != genericArgumentTypes.Length)
      {
        return QuickError($"Method '{method.Name}' requires {method.GetGenericArguments().Length} type argument(s), but {genericArgumentTypes.Length} were provided.");
      }
      try
      {
        method = method.MakeGenericMethod(genericArgumentTypes);
        Log.Debug($"[Diver] Constructed generic method: {method.Name}");
      }
      catch (Exception e)
      {
        return QuickError($"Failed to construct generic method '{method.Name}': {e.Message}", e.ToString());
      }
    }
    // Also check if the method's declaring type has unbound generic parameters
    else if (method.ContainsGenericParameters)
    {
      return QuickError($"Method '{method.Name}' or its declaring type contains unbound generic parameters. Ensure the type is a constructed generic type.");
    }


    object results = null;
    try
    {
      Log.Debug($"[Diver] Invoking {method.Name} with {paramCount} args");
      results = method.Invoke(instance, paramsArray);
    }
    catch (Exception e)
    {
      var innerEx = e;
      while (innerEx.InnerException != null)
        innerEx = innerEx.InnerException;

      Log.Debug($"[Diver] Invocation of {method.Name} failed: {innerEx.Message}");
      return QuickError($"Invocation caused exception: {innerEx.Message}", e.ToString());
    }

    InvocationResults invocResults;
    if (method.ReturnType == typeof(void))
    {
      invocResults = new InvocationResults { VoidReturnType = true };
    }
    else
    {
      if (results == null)
      {
        invocResults = new InvocationResults
        {
          VoidReturnType = false,
          ReturnedObjectOrAddress = ObjectOrRemoteAddress.Null
        };
      }
      else
      {
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
          returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name, hashCode);
        }

        invocResults = new InvocationResults
        {
          VoidReturnType = false,
          ReturnedObjectOrAddress = returnValue
        };
      }
    }

    return WrapSuccess(invocResults);
  }
}
