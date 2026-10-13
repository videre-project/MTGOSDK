/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeObjectResponse()
  {
    var request = DeserializeRequest<ObjectDumpRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    ulong objAddr = request.Address;
    string typeName = request.TypeName;
    bool pinningRequested = request.PinRequest;
    bool hashCodeFallback = request.HashcodeFallback;
    int? userHashcode = request.Hashcode;

    Log.Debug($"[Diver] Got /object request: addr={objAddr:X16}, type={typeName}, pinRequest={pinningRequested}");

    ObjectDump od;
    try
    {
      // A gone connection gets no pin (see RequestOwner).
      RequestOwner owner = CurrentRequestOwner;
      if (pinningRequested && owner.IsGone)
        throw new PinRefusedException("The request's connection has closed; no pin was taken.");
      Log.Debug($"[Diver] Calling GetHeapObject for {objAddr:X16}...");
      (object instance, ulong pinnedAddress) = _runtime.GetHeapObject(
        objAddr,
        pinningRequested,
        typeName,
        hashCodeFallback ? userHashcode : null,
        owner.Registration
      );

      od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
      od.ReferenceTaken = pinningRequested;
    }
    catch (Exception e)
    {
      return QuickError("Failed to retrieve the remote object. Error: " + e.Message);
    }

    return WrapSuccess(od);
  }
}
