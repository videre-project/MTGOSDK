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

    if (_remoteEventHandler.TryRemove(token, out RegisteredEventHandlerInfo eventInfo))
    {
      eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
      return s_okResponse;
    }
    return QuickError("Unknown token for event callback subscription");
  }
}
