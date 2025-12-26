/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeCreateObjectResponse(HttpListenerRequest arg)
  {
    Log.Debug("[Diver] Got /create_object request!");
    var body = ReadRequestBody(arg);

    if (body == null || body.Length == 0)
      return QuickError("Missing body");

    CtorInvocationRequest request;
    try
    {
      request = MessagePackSerializer.Deserialize<CtorInvocationRequest>(body);
    }
    catch
    {
      return QuickError("Failed to deserialize body");
    }

    if (request == null)
      return QuickError("Failed to deserialize body");

    Type t = _runtime.ResolveType(request.TypeFullName);
    if (t == null)
      return QuickError("Failed to resolve type");

    int paramCount = request.Parameters?.Count ?? 0;
    object[] paramsArray = new object[paramCount];

    for (int i = 0; i < paramCount; i++)
    {
      paramsArray[i] = _runtime.ParseParameterObject(request.Parameters[i]);
    }

    Log.Debug($"[Diver] Ctor'ing with {paramCount} parameters");

    object createdObject;
    try
    {
      createdObject = Activator.CreateInstance(t, paramsArray);
    }
    catch (Exception ex)
    {
      return QuickError(ex.Message, ex.ToString());
    }

    if (createdObject == null)
      return QuickError("Activator.CreateInstance returned null");

    ObjectOrRemoteAddress res;
    if (createdObject.GetType().IsPrimitiveEtc())
    {
      res = ObjectOrRemoteAddress.FromObj(createdObject);
    }
    else
    {
      ulong pinAddr = _runtime.PinObject(createdObject);
      int hashCode = createdObject.GetHashCode();
      res = ObjectOrRemoteAddress.FromToken(pinAddr, createdObject.GetType().FullName, hashCode);
    }

    var invoRes = new InvocationResults
    {
      ReturnedObjectOrAddress = res,
      VoidReturnType = false
    };

    return WrapSuccess(invoRes);
  }
}
