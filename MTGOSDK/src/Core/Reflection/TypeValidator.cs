/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Exceptions;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Provides methods for validating types.
/// </summary>
public static class TypeValidator
{
  /// <summary>
  /// Verifies that the values of two enums match.
  /// </summary>
  /// <typeparam name="TEnum1">The first enum type to validate.</typeparam>
  /// <typeparam name="TEnum2">The second enum type to validate.</typeparam>
  /// <param name="assert">Whether to throw an exception if the enums do not match.</param>
  /// <returns>True if the enums match; otherwise, false.</returns>
  /// <exception cref="ValidationException">
  /// Thrown when the enums do not match.
  /// </exception>
  public static bool ValidateEnums<TEnum1, TEnum2>(bool assert = true)
    where TEnum1 : struct, Enum
    where TEnum2 : struct, Enum
  {
    // Verify that the TEnum1 enum matches the TEnum2 enum
    if (Enum.GetNames(typeof(TEnum1)).Length !=
        Enum.GetNames(typeof(TEnum2)).Length)
    {
      if (assert)
        throw new ValidationException(
            $"The {typeof(TEnum2)} enum does not match the {typeof(TEnum1)} enum.");

      return false;
    }

    foreach (string name in Enum.GetNames(typeof(TEnum2)))
    {
      if (!Enum.TryParse<TEnum1>(name, out _))
      {
        if (assert)
          throw new ValidationException(
              $"The Setting enum is missing a '{name}' value.");

          return false;
      }
    }

    return true;
  }
}
