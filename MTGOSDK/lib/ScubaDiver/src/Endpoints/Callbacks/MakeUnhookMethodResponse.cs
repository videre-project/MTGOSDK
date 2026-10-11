/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeUnhookMethodResponse()
  {
    var request = DeserializeRequest<HookUnsubscriptionRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int token = request.Token;

    Log.Debug($"[Diver][MakeUnhookMethodResponse] Called! Token: {token}");

    lock (_methodHookGate)
    {
      if (_remoteHooks.TryGetValue(token, out RegisteredMethodHookInfo rmhi))
      {
        // Another SDK connection may still be using this shared native hook.
        if (--rmhi.SubscriptionCount > 0) return s_okResponse;

        _remoteHooks.TryRemove(token, out _);
        _activeCallbacks.TryRemove(token, out _);
        HarmonyWrapper.Instance.RemoveHook(rmhi.OriginalHookedMethod);
        return s_okResponse;
      }
    }

    Log.Debug($"[Diver][MakeUnhookMethodResponse] Unknown token for event callback subscription. Token: {token}");
    return QuickError("Unknown token for event callback subscription");
  }
}
