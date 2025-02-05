/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Serialization;

[Flags]
public enum SerializationBehavior
{
  /// <summary>
  /// The object is not serializable and will be ignored.
  /// </summary>
  Ignore,

  /// <summary>
  /// The object is not serializable and will instead be stringified.
  /// </summary>
  Stringify,

  /// <summary>
  /// The object is not serializable when used as a field and will be ignored.
  /// </summary>
  Field,

  /// <summary>
  /// The object is not serializable when used as a property and will be ignored.
  /// </summary>
  Property,
}
