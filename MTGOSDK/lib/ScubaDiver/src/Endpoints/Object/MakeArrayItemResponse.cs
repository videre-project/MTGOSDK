/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeArrayItemResponse()
  {
    var request = DeserializeRequest<IndexedItemAccessRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    ulong objAddr = request.CollectionAddress;
    object index = _runtime.ParseParameterObject(request.Index);

    if (!_runtime.TryGetPinnedObject(objAddr, out object pinnedObj))
      return QuickError("Object at given address wasn't pinned (context: ArrayItemAccess)");

    object item = null;
    if (pinnedObj.GetType().IsArray)
    {
      Array asArray = (Array) pinnedObj;
      if (index is not int intIndex)
        return QuickError("Tried to access an Array with a non-int index");

      if (intIndex >= asArray.Length)
        return QuickError("Index out of range");

      item = asArray.GetValue(intIndex);
    }
    else if (pinnedObj is IList asList)
    {
      if (index is not int intIndex)
        return QuickError("Tried to access an IList with a non-int index");

      if (intIndex >= asList.Count)
        return QuickError("Index out of range");

      item = asList[intIndex];
    }
    else if (pinnedObj is IDictionary dict)
    {
      Log.Debug("[Diver] Array access: Object is an IDICTIONARY!");
      item = dict[index];
    }
    else if (pinnedObj is IEnumerable enumerable)
    {
      if (index is not int intIndex)
        return QuickError("Tried to access an IEnumerable with a non-int index");

      // Use early-exit enumeration instead of allocating full array
      int i = 0;
      bool found = false;
      foreach (var obj in enumerable)
      {
        if (i == intIndex)
        {
          item = obj;
          found = true;
          break;
        }
        i++;
      }
      
      if (!found)
        return QuickError("Index out of range");
    }
    else
    {
      Log.Debug("[Diver] Array access: Object isn't an Array, IList, IDictionary or IEnumerable");
      return QuickError("Object isn't an Array, IList, IDictionary or IEnumerable");
    }

    ObjectOrRemoteAddress res;
    if (item == null)
    {
      res = ObjectOrRemoteAddress.Null;
    }
    else if (item.GetType().IsPrimitiveEtc())
    {
      res = ObjectOrRemoteAddress.FromObj(item);
    }
    else
    {
      ulong addr = _runtime.PinObject(item);
      int hashCode = item.GetHashCode();
      res = ObjectOrRemoteAddress.FromToken(addr, item.GetType().FullName, hashCode);
    }

    var invokeRes = new InvocationResults
    {
      VoidReturnType = false,
      ReturnedObjectOrAddress = res
    };

    return WrapSuccess(invokeRes);
  }
}
