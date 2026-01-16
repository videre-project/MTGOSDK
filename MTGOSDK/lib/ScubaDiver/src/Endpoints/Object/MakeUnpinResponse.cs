/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private static readonly byte[] s_okResponse =
    WrapSuccess(new StatusResponse { Status = "OK" });

  private byte[] MakeUnpinResponse()
  {
    var request = DeserializeRequest<UnpinRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    ulong objAddr = request.Address;

    Log.Debug($"[Diver][Debug](Unpin) objAddrStr={objAddr:X16}");
    _runtime.QueueUnpinObject(objAddr);

    return s_okResponse;
  }
}
