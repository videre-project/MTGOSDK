/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeCreateArrayResponse()
  {
    Log.Debug("[Diver] Got /create_array request!");

    var request = DeserializeRequest<ArrayCreationRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    Type elementType = _runtime.ResolveType(request.ElementTypeFullName);
    if (elementType == null)
      return QuickError($"Failed to resolve element type: {request.ElementTypeFullName}");

    // Determine array length from ConstructorArgs or explicit Length
    int length = request.ConstructorArgs?.Count ?? request.Length;
    if (length < 0)
      return QuickError($"Invalid array length: {length}");

    Log.Debug($"[Diver] Creating array of {elementType.Name}[{length}]");

    Array createdArray;
    try
    {
      createdArray = Array.CreateInstance(elementType, length);

      // If constructor args provided, construct each element
      if (request.ConstructorArgs != null && request.ConstructorArgs.Count > 0)
      {
        for (int i = 0; i < request.ConstructorArgs.Count; i++)
        {
          var ctorArgs = request.ConstructorArgs[i];
          object[] paramsArray = new object[ctorArgs?.Count ?? 0];

          for (int j = 0; j < paramsArray.Length; j++)
          {
            paramsArray[j] = _runtime.ParseParameterObject(ctorArgs[j]);
          }

          // Create instance using Activator
          object element = Activator.CreateInstance(elementType, paramsArray);
          createdArray.SetValue(element, i);
        }
      }
    }
    catch (Exception ex)
    {
      return QuickError(ex.Message, ex.ToString());
    }

    if (createdArray == null)
      return QuickError("Array.CreateInstance returned null");

    ulong pinAddr = _runtime.PinObject(createdArray);
    int hashCode = createdArray.GetHashCode();
    var res = ObjectOrRemoteAddress.FromToken(
      pinAddr,
      createdArray.GetType().FullName,
      hashCode);

    var invoRes = new InvocationResults
    {
      ReturnedObjectOrAddress = res,
      VoidReturnType = false
    };

    return WrapSuccess(invoRes);
  }
}
