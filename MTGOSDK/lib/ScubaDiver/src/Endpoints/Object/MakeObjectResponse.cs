/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeObjectResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    string typeName = arg.QueryString.Get("type_name");
    bool pinningRequested = arg.QueryString.Get("pinRequest")?.ToUpperInvariant() == "TRUE";
    bool hashCodeFallback = arg.QueryString.Get("hashcode_fallback")?.ToUpperInvariant() == "TRUE";
    string hashCodeStr = arg.QueryString.Get("hashcode");
    int userHashcode = 0;

    Log.Debug($"[Diver] Got /object request: addr={objAddrStr}, type={typeName}, pinRequest={pinningRequested}");

    if (objAddrStr == null)
      return QuickError("Missing parameter 'address'");

    if (!ulong.TryParse(objAddrStr, out var objAddr))
      return QuickError("Parameter 'address' could not be parsed as ulong");

    if (hashCodeFallback && !int.TryParse(hashCodeStr, out userHashcode))
      return QuickError("Parameter 'hashcode_fallback' was 'true' but the hashcode argument was missing or not an int");

    ObjectDump od;
    try
    {
      Log.Debug($"[Diver] Calling GetHeapObject for {objAddrStr}...");
      (object instance, ulong pinnedAddress) = _runtime.GetHeapObject(
        objAddr,
        pinningRequested,
        typeName,
        hashCodeFallback ? userHashcode : null
      );

      od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
    }
    catch (Exception e)
    {
      return QuickError("Failed to retrieve the remote object. Error: " + e.Message);
    }

    return WrapSuccess(od);
  }
}
