/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

[MessagePackObject]
public class CallbackInvocationRequest
{
  [Key(0)]
  public DateTime Timestamp { get; set; }

  [Key(1)]
  public int Token { get; set; }

  [Key(2)]
  public List<ObjectOrRemoteAddress> Parameters { get; set; } = new();

  [Key(3)]
  public RemoteBatchResult Capture { get; set; }

  [Key(4)]
  public RemoteBatchError CaptureError { get; set; }

  [Key(5)]
  public RemoteCaptureSequence CaptureSequence { get; set; }

  /// <summary>
  /// Set by a Diver that pins each object parameter once for every client it
  /// sends the callback to: the receiving client owns one reference to each and
  /// returns what it does not keep. A Diver without it (false when absent) pins
  /// once for all recipients: a client then returns no reference for an
  /// argument it does not wrap, and a new proxy adopts the shared reference
  /// (returned when the proxy is released), as before.
  /// </summary>
  [Key(6)]
  public bool ParametersPinnedPerRecipient { get; set; }
}
