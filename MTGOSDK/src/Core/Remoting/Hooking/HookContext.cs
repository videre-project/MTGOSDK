/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2022, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace MTGOSDK.Core.Remoting.Hooking;

public class HookContext(DateTime timestamp)
{
  public DateTime Timestamp => timestamp;

  /// <summary>
  /// Detached values captured before native callback delivery was queued.
  /// </summary>
  public RemoteBatchResult? Capture { get; init; }

  /// <summary>
  /// A request-level capture failure, distinct from errors in returned rows.
  /// </summary>
  public RemoteBatchError? CaptureError { get; init; }

  /// <summary>
  /// Native stream identity and completion order for enrolled captures, including
  /// standalone notifications outside a native invocation scope.
  /// </summary>
  public RemoteCaptureSequence? CaptureSequence { get; init; }

  /// <summary>
  /// Whether this client owns a pin reference to each object parameter (the
  /// Diver pinned them for every recipient) and so must take each over or
  /// return it. False for a Diver that pins once for all recipients: an
  /// argument that is not wrapped returns nothing, and a new proxy adopts the
  /// shared reference, as before.
  /// </summary>
  internal bool OwnsParameters { get; init; }
}
