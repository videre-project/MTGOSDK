/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;

using MTGOSDK.Core.Reflection.Extensions;


namespace MTGOSDK.Core.Compiler.Extensions;

/// <summary>
/// Provides methods for parsing stack frames and tracing callers.
/// </summary>
public static class CallerExtensions
{
  /// <summary>
  /// Gets the name of the caller.
  /// </summary>
  /// <param name="depth">The stack frame depth.</param>
  /// <returns>The name of the caller.</returns>
  public static string GetCallerName(int depth) =>
    new StackFrame(depth).GetMethod().Name
      .Replace("get_", "")
      .Replace("set_", "");

  /// <summary>
  /// Gets the parent type of the caller.
  /// </summary>
  /// <param name="depth">The stack frame depth.</param>
  /// <returns>The type of the caller.</returns>
  public static Type GetCallerType(int depth) =>
    new StackFrame(depth).GetMethod().ReflectedType;

  /// <summary>
  /// Gets the stack frame depth of the caller.
  /// </summary>
  /// <param name="depth">The starting stack frame depth.</param>
  /// <returns>The caller's stack frame depth.</returns>
  public static int GetCallerDepth(int depth = 3)
  {
    Type wrapperType = GetCallerType(depth);
    while(GetCallerType(depth).Name == wrapperType.Name && depth < 50) depth++;

    return depth;
  }

  /// <summary>
  /// Gets all members of the caller that have a specific attribute.
  /// </summary>
  private static MemberAttributePair<T>[] GetCallerAttributes<T>(int depth = 2)
      where T : Attribute =>
    GetCallerType(depth).GetMemberAttributes<T>();

  /// <summary>
  /// Gets a specific attribute of the caller, if it exists.
  /// </summary>
  /// <typeparam name="T">The type of attribute.</typeparam>
  /// <param name="depth">The stack frame depth.</param>
  /// <returns>The attribute, or null if it does not exist.</returns>
  public static T? GetCallerAttribute<T>(int depth = 2) where T : Attribute
  {
    string name = GetCallerName(depth);
    try
    {
      foreach (var memberAttributePair in GetCallerAttributes<T>(depth+1))
      {
        if (memberAttributePair.Member.Name == name)
          return memberAttributePair.Attribute;
      }
    }
    // Invalid member access (or otherwise doesn't have any attributes)
    catch (NullReferenceException) { }

    return null;
  }
}
