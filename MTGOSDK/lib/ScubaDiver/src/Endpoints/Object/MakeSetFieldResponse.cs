/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Net;

using Microsoft.Diagnostics.Runtime;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeSetFieldResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /set_field request!");
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

    if (request.ObjAddress == 0)
      return QuickError("Can't set field of a null target");

    Type dumpedObjType;
    object instance;

    if (_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
    {
      dumpedObjType = instance.GetType();
    }
    else
    {
      ClrObject clrObj = default;
      lock (_runtime.clrLock)
      {
        clrObj = _runtime.GetClrObject(request.ObjAddress);
        if (clrObj.Type == null)
          return QuickError($"The invalid address for '{request.TypeFullName}'.");
      }
      if (clrObj.Type == null)
        return QuickError($"The address for '{request.TypeFullName}' moved since last refresh.");

      ulong mt = clrObj.Type.MethodTable;
      dumpedObjType = _runtime.ResolveType(clrObj.Type.Name);
      try
      {
        instance = _runtime.Compile(clrObj.Address, mt);
      }
      catch (Exception)
      {
        return QuickError("Couldn't get handle to requested object. It could be because the Method Table or a GC collection happened.");
      }
    }

    var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
    if (fieldInfo == null)
    {
      Debugger.Launch();
      Log.Debug("[Diver] Failed to Resolved field :/");
      return QuickError("Couldn't find field in type.");
    }

    Log.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

    object results;
    try
    {
      object value = _runtime.ParseParameterObject(request.Value);
      fieldInfo.SetValue(instance, value);
      results = fieldInfo.GetValue(instance);
    }
    catch (Exception e)
    {
      return QuickError($"Invocation caused exception: {e}");
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
