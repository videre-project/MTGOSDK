/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Runtime.CompilerServices;

namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Equality comparer that treats Type keys by reference identity.
/// Avoids invoking potentially overridden GetHashCode/Equals on remote Type proxies.
/// </summary>
public sealed class TypeReferenceEqualityComparer : IEqualityComparer<Type>
{
  public static readonly TypeReferenceEqualityComparer Instance = new();

  public bool Equals(Type x, Type y) => ReferenceEquals(x, y);

  public int GetHashCode(Type obj) => RuntimeHelpers.GetHashCode(obj);
}
