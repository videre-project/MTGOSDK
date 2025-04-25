/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using static System.Collections.StructuralComparisons;


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

public class MethodSigHash(string name, Type[] parameters)
{
  public readonly string Name = name;
  public readonly Type[] Parameters = parameters;

  public MethodSigHash(MethodInfo mi)
    : this(mi.Name, mi.GetParameters().Select(i => i.ParameterType).ToArray())
  {
  }

  public bool Equals(MethodSigHash other)
  {
    if (ReferenceEquals(null, other)) return false;
    if (ReferenceEquals(this, other)) return true;
    return Equals(other.Name, Name) &&
      StructuralEqualityComparer.Equals(other.Parameters, Parameters);
  }

  public override bool Equals(object obj)
  {
    if (ReferenceEquals(null, obj)) return false;
    if (ReferenceEquals(this, obj)) return true;
    if (obj.GetType() != typeof(MethodSigHash)) return false;
    return Equals((MethodSigHash)obj);
  }

  public override int GetHashCode()
  {
    unchecked
    {
      return (Name.GetHashCode() * 397) ^
        StructuralEqualityComparer.GetHashCode(Parameters);
    }
  }
}
