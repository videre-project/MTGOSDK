/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

/// <summary>
/// Identifies a failed operation and, for projection failures, its output path.
/// Capture failures outside a row use operation ID -1.
/// </summary>
[MessagePackObject]
public sealed class RemoteBatchError
{
  [Key(0)]
  public int OperationId { get; set; }
  [Key(1)]
  public string? OutputPath { get; set; }
  [Key(2)]
  public string ExceptionType { get; set; } = "";
  [Key(3)]
  public string Message { get; set; } = "";
  [Key(4)]
  public string? StackTrace { get; set; }
}
