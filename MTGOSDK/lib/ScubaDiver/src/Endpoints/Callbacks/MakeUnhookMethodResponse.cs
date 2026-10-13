/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


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

    // A share this client no longer holds was released with its registration
    // (or is another client's): nothing is left to do.
    if (TakeClientSubscription(token, hook: true) == SubscriptionOwner.NotOwned)
      return s_okResponse;
    if (RemoveHookShare(token))
      return s_okResponse;

    Log.Debug($"[Diver][MakeUnhookMethodResponse] Unknown token for event callback subscription. Token: {token}");
    return QuickError("Unknown token for event callback subscription");
  }
}
