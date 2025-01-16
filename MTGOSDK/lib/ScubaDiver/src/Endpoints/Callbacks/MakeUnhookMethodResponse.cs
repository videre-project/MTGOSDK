/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


using System;
using System.Net;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeUnhookMethodResponse(HttpListenerRequest arg)
  {
    string tokenStr = arg.QueryString.Get("token");
    if (tokenStr == null || !int.TryParse(tokenStr, out int token))
    {
      return QuickError("Missing parameter 'address'");
    }
    Log.Debug($"[Diver][MakeUnhookMethodResponse] Called! Token: {token}");
    if (_remoteHooks.TryRemove(token, out RegisteredMethodHookInfo rmhi))
    {
      HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
      return "{\"status\":\"OK\"}";
    }
    Log.Debug($"[Diver][MakeUnhookMethodResponse] Unknown token for event callback subscription. Token: {token}");
    return QuickError("Unknown token for event callback subscription");
  }
}
