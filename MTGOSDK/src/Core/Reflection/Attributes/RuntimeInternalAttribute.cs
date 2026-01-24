/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Attributes;

/// <summary>
/// A wrapper attribute that indicates that a field is internal to the runtime.
/// </summary>
/// <param name="baseType">The base type of the attribute.</param>
public class RuntimeInternalAttribute(Type? baseType = null)
    : CallerAttribute<RuntimeInternalAttribute>
{
  public readonly Type BaseType = baseType ?? typeof(object);
}
