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
  private byte[] MakeEventUnsubscribeResponse()
  {
    var request = DeserializeRequest<EventUnsubscriptionRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int token = request.Token;

    Log.Debug($"[Diver][MakeEventUnsubscribeResponse] Called! Token: {token}");

    // Only the client that subscribed (or a connection that never registered)
    // removes a subscription. One this client no longer holds was removed
    // with its registration (or is another client's): nothing is left to do.
    if (TakeClientSubscription(token, hook: false) == SubscriptionOwner.NotOwned)
      return s_okResponse;
    if (RemoveEventSubscription(token))
      return s_okResponse;

    Log.Debug($"[Diver][MakeEventUnsubscribeResponse] Unknown token for event callback subscription. Token: {token}");
    return QuickError("Unknown token for event callback subscription");
  }
}
