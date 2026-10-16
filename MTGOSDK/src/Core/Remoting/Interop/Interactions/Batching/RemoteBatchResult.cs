/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Contains root outputs and collection rows returned by one batch execution.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
public sealed class RemoteBatchResult : IPinResponse
{
  [IgnoreMember]
  string? IPinResponse.PinOwner
  {
    set
    {
      if (Reference is not null)
        Reference.PinOwner = value;
    }
  }

  [Key(0)]
  public Dictionary<string, RemoteBatchValue> Values { get; set; } = new();

  [Key(1)]
  public List<RemoteBatchRow> Rows { get; set; } = [];

  /// <summary>
  /// The explicitly retained root object, whose lifetime follows remote references.
  /// Detached outputs and row intermediates are not pinned.
  /// </summary>
  [Key(2)]
  public ObjectOrRemoteAddress? Reference { get; set; }
}
