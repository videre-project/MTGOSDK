/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;


namespace MTGOSDK.Win32.Utilities;

/// <summary>
/// Provides a simple interface for accessing the Windows registry.
/// </summary>
public static class RegistryStore
{
  /// <summary>
  /// Get the registry key from the specified key path.
  /// </summary>
  /// <param name="keyPath">The registry key path.</param>
  /// <returns>A registry key object.</returns>
  public static RegistryKey? GetUserRegistryKey(string keyPath)
  {
    return Registry.CurrentUser.OpenSubKey(keyPath, false);
  }

  /// <summary>
  /// Get the registry token from the specified key path and value name.
  /// </summary>
  /// <param name="keyPath">The registry key path.</param>
  /// <param name="valueName">The registry value name.</param>
  /// <returns>A string containing the registry token.</returns>
  public static string? GetRegistryToken(string keyPath, string valueName)
  {
    using (RegistryKey? key = GetUserRegistryKey(keyPath))
    {
      if (key != null)
      {
        var token = (key.GetValue(valueName) as string)!;
        key.Close();

        return @$"{token[..8]}.{token[8..11]}\{token[11..19]}.{token[19..22]}";
      }
    }

    return null;
  }
}
