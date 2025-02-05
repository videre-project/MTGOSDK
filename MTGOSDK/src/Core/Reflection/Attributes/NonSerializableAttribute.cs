/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection.Serialization;


namespace MTGOSDK.Core.Reflection.Attributes;

/// <summary>
/// Marks a class type or instance as non-serializable when used as a field or property.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field)]
public class NonSerializableAttribute(
  SerializationBehavior behavior = SerializationBehavior.Field |
                                  SerializationBehavior.Property)
    : Attribute
{
  public SerializationBehavior Behavior => behavior;
}
