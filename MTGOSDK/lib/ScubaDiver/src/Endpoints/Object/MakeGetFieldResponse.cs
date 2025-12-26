/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeGetFieldResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /get_field request!");
    var body = ReadRequestBody(arg);

    if (body == null || body.Length == 0)
      return QuickError("Missing body");

    FieldSetRequest request;
    try
    {
      request = MessagePackSerializer.Deserialize<FieldSetRequest>(body);
    }
    catch
    {
      return QuickError("Failed to deserialize body");
    }

    if (request == null)
      return QuickError("Failed to deserialize body");

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

      var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
      if (fieldInfo == null)
      {
        Debugger.Launch();
        Log.Debug("[Diver] Failed to Resolved field :/");
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

    var invocResults = new InvocationResults
    {
      VoidReturnType = false,
      ReturnedObjectOrAddress = returnValue
    };

    return WrapSuccess(invocResults);
  }
}
