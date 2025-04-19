/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// Specifies stack trace filter patterns to be applied at the assembly level.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ExceptionFilterAttribute(params string[] filterPatterns)
    : Attribute
{
  /// <summary>
  /// Gets the collection of filter patterns used for stack trace filtering.
  /// </summary>
  public string[] FilterPatterns { get; } = filterPatterns;
}
