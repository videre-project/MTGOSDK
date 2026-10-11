/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

/// <summary>
/// Selects native invocations by instance type and a string argument before capture.
/// </summary>
[MessagePackObject]
public sealed class RemoteHookFilter
{
  /// <summary>
  /// The native instance type to match, including its derived types.
  /// </summary>
  [Key(0)]
  public string TypeFullName { get; set; } = "";

  /// <summary>
  /// An optional member path whose value replaces the instance batch input.
  /// A null value at this path excludes the invocation.
  /// </summary>
  [Key(1)]
  public string? InstancePath { get; set; }

  /// <summary>
  /// The string parameter to compare with ArgumentValues.
  /// </summary>
  [Key(2)]
  public int ArgumentIndex { get; set; }

  /// <summary>
  /// Accepted argument values, compared using ordinal equality.
  /// </summary>
  [Key(3)]
  public string?[] ArgumentValues { get; set; } = [];
}
