/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Attributes;
/// <summary>
/// A wrapper attribute that allows for a default value to fallback to.
/// </summary>
/// <param name="value">The default value.</param>
public class DefaultAttribute(object value) : CallerAttribute<DefaultAttribute>
{
  public readonly object Value = value;
}
