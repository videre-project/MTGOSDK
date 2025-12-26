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
  private byte[] MakeUnhookMethodResponse(HttpListenerRequest arg)
  {
    string tokenStr = arg.QueryString.Get("token");
    if (tokenStr == null || !int.TryParse(tokenStr, out int token))
      return QuickError("Missing parameter 'token'");

    Log.Debug($"[Diver][MakeUnhookMethodResponse] Called! Token: {token}");

    if (_remoteHooks.TryRemove(token, out RegisteredMethodHookInfo rmhi))
    {
      if (_callbackTokens.TryRemove(token, out var tokenSource))
      {
        tokenSource.Cancel();
        tokenSource.Dispose();
      }
      HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
      return s_okResponse;
    }

    Log.Debug($"[Diver][MakeUnhookMethodResponse] Unknown token for event callback subscription. Token: {token}");
    return QuickError("Unknown token for event callback subscription");
  }
}
