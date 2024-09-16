/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeEventUnsubscribeResponse(HttpListenerRequest arg)
  {
    string tokenStr = arg.QueryString.Get("token");
    if (tokenStr == null || !int.TryParse(tokenStr, out int token))
    {
      return QuickError("Missing parameter 'address'");
    }
    Log.Debug($"[Diver][MakeEventUnsubscribeResponse] Called! Token: {token}");

    if (_remoteEventHandler.TryRemove(token, out RegisteredEventHandlerInfo eventInfo))
    {
      eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
      return "{\"status\":\"OK\"}";
    }
    return QuickError("Unknown token for event callback subscription");
  }
}
