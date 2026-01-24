/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Attributes;

/// <summary>
/// A wrapper attribute that allows for a default value to fallback to.
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
public class CallerAttribute<T> : Attribute where T : Attribute
{
  /// <summary>
  /// Attempts to get the caller attribute from the outer caller.
  /// </summary>
  /// <param name="attribute">The caller attribute (if present).</param>
  /// <returns>True if the caller attribute was found.</returns>
  public static bool TryGetCallerAttribute(out T? attribute)
  {
    attribute = GetCallerAttribute<T>(depth: GetCallerDepth());
    return attribute != null;
  }
}
