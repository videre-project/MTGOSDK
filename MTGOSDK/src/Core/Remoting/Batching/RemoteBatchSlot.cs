/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.Core.Remoting.Batching;

/// <summary>
/// A reference to an input, result, or collection item in a locally recorded batch.
/// </summary>
public sealed class RemoteBatchSlot
{
  internal RemoteBatch Owner { get; }
  internal RemoteBatchScope Scope { get; }
  internal RemoteBatchReference Reference { get; }

  internal RemoteBatchSlot(RemoteBatch owner, RemoteBatchScope scope,
    RemoteBatchReference reference)
  {
    Owner = owner;
    Scope = scope;
    Reference = reference;
  }
}
