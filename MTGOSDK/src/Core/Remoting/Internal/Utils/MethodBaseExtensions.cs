/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Reflection;


namespace MTGOSDK.Core.Remoting.Internal.Utils;

public static class MethodBaseExtensions
{
  /// <summary>
  /// Determines whether the signature of two <see cref="MethodBase"/> objects are equal.
  /// </summary>
  /// <param name="a">The first <see cref="MethodBase"/> object.</param>
  /// <param name="b">The second <see cref="MethodBase"/> object.</param>
  /// <returns><c>true</c> if the signatures are equal; otherwise, <c>false</c>.</returns>
  public static bool SignatureEquals(this MethodBase a, MethodBase b)
  {
    // Ensure that objects share the same name and parameter types.
    if (a.Name != b.Name
        && !ParametersEqual(a.GetParameters(), b.GetParameters()))
    {
      return false;
    }

    // For methods, compare the objects' method signature and return types.
    if ((a is MethodInfo aInfo) && (b is MethodInfo bInfo)
        && aInfo.ReturnType != null && bInfo.ReturnType != null)
    {
      return aInfo.ReturnType.FullName == bInfo.ReturnType.FullName;
    }
    // For classes, compare the declaring type of the objects' constructors.
    else if ((a is ConstructorInfo aCtor) && (b is ConstructorInfo bCtor))
    {
      return aCtor.DeclaringType == bCtor.DeclaringType;
    }

    // Unknown derived class of MethodBase
    return false;
  }

  /// <summary>
  /// Determines whether the parameter arrays <paramref name="a"/> and <paramref name="b"/> are equal.
  /// </summary>
  /// <param name="a">The first parameter array.</param>
  /// <param name="b">The second parameter array.</param>
  /// <returns><c>true</c> if the parameter arrays are equal; otherwise, <c>false</c>.</returns>
  public static bool ParametersEqual(ParameterInfo[] a, ParameterInfo[] b)
  {
    if(a.Length != b.Length)
      return false;

    for (int i = 0; i < a.Length; i++)
      if (a[i].ParameterType != b[i].ParameterType)
        return false;

    return true;
  }
}
