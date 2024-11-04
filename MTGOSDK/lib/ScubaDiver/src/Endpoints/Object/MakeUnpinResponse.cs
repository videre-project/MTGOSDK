/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MTGOSDK.Core.Logging;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeUnpinResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
    {
      return QuickError("Missing parameter 'address'");
    }
    Log.Debug($"[Diver][Debug](Unpin) objAddrStr={objAddr:X16}");

    // Remove if we have this object in our pinned pool, otherwise ignore.
    _runtime.UnpinObject(objAddr);

    return "{\"status\":\"OK\"}";
  }
}
