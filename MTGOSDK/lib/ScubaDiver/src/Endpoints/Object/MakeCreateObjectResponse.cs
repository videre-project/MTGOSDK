/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeCreateObjectResponse()
  {
    Log.Debug("[Diver] Got /create_object request!");

    var request = DeserializeRequest<CtorInvocationRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    Type t = _runtime.ResolveType(request.TypeFullName);
    if (t == null)
      return QuickError("Failed to resolve type");

    int paramCount = request.Parameters?.Count ?? 0;
    object[] paramsArray = new object[paramCount];

    for (int i = 0; i < paramCount; i++)
    {
      paramsArray[i] = _runtime.ParseParameterObject(request.Parameters[i]);
    }

    Log.Debug($"[Diver] Ctor'ing with {paramCount} parameters (ForceUIThread={request.ForceUIThread})");

    // Check if this type requires UI thread affinity (DispatcherObject-derived types)
    // These MUST be created on UI thread because they have thread affinity - 
    // creating on worker thread then retrying operations on UI thread doesn't work.
    bool isDispatcherObject = typeof(System.Windows.Threading.DispatcherObject).IsAssignableFrom(t);
    bool needsUIThread = request.ForceUIThread && isDispatcherObject;

    object createdObject;
    try
    {
      if (needsUIThread && !STAThread.IsDispatcherThread)
      {
        // For DispatcherObject types with ForceUIThread, create on UI thread proactively
        Log.Debug($"[Diver] Creating DispatcherObject {t.Name} on UI thread (thread affinity required)");
        createdObject = STAThread.Execute(() => Activator.CreateInstance(t, paramsArray));
      }
      else
      {
        // Try creating the object on the current thread first - most don't need UI thread
        createdObject = Activator.CreateInstance(t, paramsArray);
      }
    }
    catch (Exception ex) when (STAThread.RequiresSTAThread(ex) || 
                               (ex.InnerException != null && STAThread.RequiresSTAThread(ex.InnerException)))
    {
      // Retry on STA/UI thread for types that throw on worker thread
      Log.Debug($"[Diver] Retrying CreateInstance on STA thread due to: {ex.InnerException?.Message ?? ex.Message}");
      try
      {
        createdObject = STAThread.Execute(() => Activator.CreateInstance(t, paramsArray));
      }
      catch (Exception retryEx)
      {
        return QuickError(retryEx.Message, retryEx.ToString());
      }
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
