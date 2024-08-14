/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Compiler.Snapshot;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeArrayItemResponse(HttpListenerRequest arg)
  {
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
      return QuickError("Missing body");

    var request = JsonConvert.DeserializeObject<IndexedItemAccessRequest>(body);
    if (request == null)
      return QuickError("Failed to deserialize body");

    ulong objAddr = request.CollectionAddress;
    object index = _runtime.ParseParameterObject(request.Index);
    bool pinningRequested = request.PinRequest;

    // Check if we have this objects in our pinned pool
    if (!_runtime.TryGetPinnedObject(objAddr, out object pinnedObj))
    {
      // Object not pinned, try get it the hard way
      return QuickError("Object at given address wasn't pinned (context: ArrayItemAccess)");
    }

    object item = null;
    if (pinnedObj.GetType().IsArray)
    {
      Array asArray = (Array)pinnedObj;
      if (index is not int intIndex)
        return QuickError("Tried to access an Array with a non-int index");

      int length = asArray.Length;
      if (intIndex >= length)
        return QuickError("Index out of range");

      item = asArray.GetValue(intIndex);
    }
    else if (pinnedObj is IList asList)
    {
      object[] asArray = asList?.Cast<object>().ToArray();
      if (asArray == null)
        return QuickError("Object at given address seemed to be an IList but failed to convert to array");

      if (index is not int intIndex)
        return QuickError("Tried to access an IList with a non-int index");

      int length = asArray.Length;
      if (intIndex >= length)
        return QuickError("Index out of range");

      // Get the item
      item = asArray[intIndex];
    }
    else if (pinnedObj is IDictionary dict)
    {
      Log.Debug("[Diver] Array access: Object is an IDICTIONARY!");
      item = dict[index];
    }
    else if (pinnedObj is IEnumerable enumerable)
    {
      // Last result - generic IEnumerables can be enumerated into arrays.
      // BEWARE: This could lead to "ruining" of the IEnumerable if it's a not "resetable"
      object[] asArray = enumerable?.Cast<object>().ToArray();
      if (asArray == null)
        return QuickError("Object at given address seemed to be an IEnumerable but failed to convert to array");

      if (index is not int intIndex)
        return QuickError("Tried to access an IEnumerable (which isn't an Array, IList or IDictionary) with a non-int index");

      int length = asArray.Length;
      if (intIndex >= length)
        return QuickError("Index out of range");

      // Get the item
      item = asArray[intIndex];
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
      // TODO: Something else?
      res = ObjectOrRemoteAddress.FromObj(item);
    }
    else
    {
      // Non-primitive results must be pinned before returning their remote address
      // TODO: If a RemoteObject is not created for this object later and the item is not automatically unfreezed it might leak.
      ulong addr = _runtime.PinObject(item);

      res = ObjectOrRemoteAddress.FromToken(addr, item.GetType().FullName);
    }

    InvocationResults invokeRes = new()
    {
      VoidReturnType = false,
      ReturnedObjectOrAddress = res
    };

    return JsonConvert.SerializeObject(invokeRes);
  }
}
