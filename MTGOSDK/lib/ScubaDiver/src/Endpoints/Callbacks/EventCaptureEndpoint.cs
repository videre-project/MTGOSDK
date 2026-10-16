/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MessagePack;

using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace ScubaDiver;

/// <summary>
/// Requests one ordered capture from an existing native event subscription.
/// The response identifies the completion queued for callback delivery.
/// </summary>
internal sealed class EventCaptureEndpoint(
  Func<int, Func<RemoteCaptureSequence>> getCapture,
  Func<Func<RemoteCaptureSequence>, RemoteCaptureSequence> dispatch)
{
  public byte[] Handle(byte[] body)
  {
    try
    {
      var request = MessagePackSerializer.Deserialize<EventCaptureRequest>(body);
      var capture = getCapture(request.Token);
      var sequence = request.ForceUIThread ? dispatch(capture) : capture();
      return MessagePackSerializer.Serialize(DiverResponse<RemoteCaptureSequence>.Success(sequence));
    }
    catch (Exception error)
    {
      return MessagePackSerializer.Serialize(
        DiverResponse<RemoteCaptureSequence>.FromError(error.Message, error.ToString()));
    }
  }
}
