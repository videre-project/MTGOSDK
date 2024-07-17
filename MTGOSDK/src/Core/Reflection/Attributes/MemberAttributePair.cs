/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;


namespace MTGOSDK.Core.Reflection.Attributes;

/// <summary>
/// A struct that contains a member and its attribute.
/// </summary>
/// <typeparam name="T">The type of attribute.</typeparam>
public struct MemberAttributePair<T>() where T : Attribute
{
  public MemberInfo Member { get; init; }
  public T Attribute { get; init; }
}
