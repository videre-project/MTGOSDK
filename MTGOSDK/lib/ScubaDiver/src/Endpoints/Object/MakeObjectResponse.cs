/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;
using System.Threading;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeObjectResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    string typeName = arg.QueryString.Get("type_name");
    bool pinningRequested = arg.QueryString.Get("pinRequest").ToUpper() == "TRUE";
    bool hashCodeFallback = arg.QueryString.Get("hashcode_fallback").ToUpper() == "TRUE";
    string hashCodeStr = arg.QueryString.Get("hashcode");
    int userHashcode = 0;

    Log.Debug($"[Diver] Got /object request: addr={objAddrStr}, type={typeName}, pinRequest={pinningRequested}");

    if (objAddrStr == null)
    {
      return QuickError("Missing parameter 'address'");
    }
    if (!ulong.TryParse(objAddrStr, out var objAddr))
    {
      return QuickError("Parameter 'address' could not be parsed as ulong");
    }
    if (hashCodeFallback)
    {
      if (!int.TryParse(hashCodeStr, out userHashcode))
      {
        return QuickError("Parameter 'hashcode_fallback' was 'true' but the hashcode argument was missing or not an int");
      }
    }

    // Attempt to dump the object and remote type
    ObjectDump od = null!;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
      Log.Debug($"[Diver] Calling GetHeapObject for {objAddrStr}...");
      (object instance, ulong pinnedAddress) = _runtime.GetHeapObject(
        objAddr,
        pinningRequested,
        typeName,
        hashCodeFallback ? userHashcode : null
      );
      Log.Debug($"[Diver] GetHeapObject completed in {sw.ElapsedMilliseconds}ms");

      sw.Restart();
      od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
      Log.Debug($"[Diver] ObjectDumpFactory.Create completed in {sw.ElapsedMilliseconds}ms");
    }
    catch (Exception e)
    {
      return QuickError("Failed to retrieve the remote object. Error: " + e.Message);
    }

    sw.Restart();
    var json = JsonConvert.SerializeObject(od);
    Log.Debug($"[Diver] JSON serialization completed in {sw.ElapsedMilliseconds}ms, size={json.Length} bytes");
    return json;
  }
}
