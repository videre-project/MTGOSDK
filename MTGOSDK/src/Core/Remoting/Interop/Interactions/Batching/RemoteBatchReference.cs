/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// References a declared input, an earlier operation result, or an enclosing collection item.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchReference
{
  [Key(0)]
  public RemoteBatchReferenceKind Kind { get; set; }
  [Key(1)]
  public int Id { get; set; }
}
