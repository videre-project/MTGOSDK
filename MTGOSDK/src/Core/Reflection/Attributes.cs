/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Linq;
using System.Reflection;


namespace MTGOSDK.Core.Reflection;

public static class Attributes
{
  //
  // Member Attribute Reflection
  //

  /// <summary>
  /// A struct that contains a member and its attribute.
  /// </summary>
  /// <typeparam name="T">The type of attribute.</typeparam>
  public struct MemberAttributePair<T>() where T : Attribute
  {
    public MemberInfo Member { get; init; }
    public T Attribute { get; init; }
  }

  /// <summary>
  /// Gets all members of a type that have a specific attribute.
  /// </summary>
  /// <typeparam name="T">The type of attribute.</typeparam>
  /// <param name="type">The type to get members from.</param>
  /// <param name="bindingFlags">The binding flags to use.</param>
  /// <returns>An array of member attribute pairs.</returns>
  public static MemberAttributePair<T>[] GetMemberAttributes<T>(
    this Type type,
    BindingFlags bindingFlags = BindingFlags.Public
                              | BindingFlags.NonPublic
                              | BindingFlags.Instance
                              | BindingFlags.Static
                              | BindingFlags.FlattenHierarchy
  ) where T : Attribute =>
    type.GetMembers(bindingFlags)
      .Where(p => p.IsDefined(typeof(T), false))
      .Select(p => new MemberAttributePair<T>()
      {
        Member = p,
#pragma warning disable CS8601 // Possible null reference argument.
        Attribute = p.GetCustomAttributes(typeof(T), false).Single() as T
#pragma warning restore CS8601
      })
      .ToArray();

  //
  // StackFrame Property Reflection
  //

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

  public static MemberAttributePair<T>[] GetMemberAttributes<T>(
    int depth = 2
  ) where T : Attribute =>
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
      foreach (var memberAttributePair in GetMemberAttributes<T>(depth+1))
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
