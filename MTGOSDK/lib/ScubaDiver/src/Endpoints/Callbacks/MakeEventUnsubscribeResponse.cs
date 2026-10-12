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
    // removes a subscription; one removed with its registration is unknown.
    if (TakeClientSubscription(token, hook: false) != SubscriptionOwner.NotOwned &&
        RemoveEventSubscription(token))
      return s_okResponse;
    return QuickError("Unknown token for event callback subscription");
  }
}
