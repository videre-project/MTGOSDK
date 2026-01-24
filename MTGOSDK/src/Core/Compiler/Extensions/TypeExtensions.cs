/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Runtime.CompilerServices;


namespace MTGOSDK.Core.Compiler.Extensions;

public static class TypeExtensions
{
  /// <summary>
  /// Determines whether the type is compiler-generated.
  /// </summary>
  /// <param name="t">The type to check.</param>
  /// <returns>True if the type is compiler-generated, false otherwise.</returns>
  public static bool IsCompilerGenerated(this Type t)
  {
    if (t == null) return false;

    return t.IsDefined(typeof(CompilerGeneratedAttribute), false)
      || IsCompilerGenerated(t.DeclaringType);
  }

  /// <summary>
  /// Extracts the base type from a compiler-generated type.
  /// </summary>
  /// <param name="t">The type to extract the base type from.</param>
  /// <returns>The base type of the given type.</returns>
  public static Type GetBaseType(this Type t)
  {
    if (!t.IsCompilerGenerated()) return t;

    string fullName = t.FullName;
    string baseName = fullName.Substring(0, fullName.IndexOf("+<"));
    Type baseType = t.DeclaringType.Assembly.GetType(baseName);

    return baseType;
  }
}
