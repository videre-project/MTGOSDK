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
/// Provides utility methods for interoping with ClickOnce deployment.
/// </summary>
public static class DeploymentUtilities
{
  /// <summary>
  /// The registry key path for the WinSxS component store.
  /// </summary>
  private const string SIDEBYSIDE_REGISTRY_KEY_PATH =
    @"SOFTWARE\Classes\Software\Microsoft\Windows\CurrentVersion\Deployment\SideBySide\2.0";

  /// <summary>
  /// The registry key path for the WinSxS State Manager.
  /// </summary>
  private const string SIDEBYSIDE_STATE_MANAGER_REGISTRY_KEY_PATH =
    @"SOFTWARE\Classes\Software\Microsoft\Windows\CurrentVersion\Deployment\SideBySide\2.0\StateManager";

  /// <summary>
  /// The application directory for ClickOnce deployments.
  /// </summary>
  public static string ApplicationDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    @"Apps\2.0",
    GetRegistryToken(SIDEBYSIDE_REGISTRY_KEY_PATH,
                     "ComponentStore_RandomString")
  );

  /// <summary>
  /// The application data directory for ClickOnce user data.
  /// </summary>
  public static string ApplicationDataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    @"Apps\2.0\Data",
    GetRegistryToken(SIDEBYSIDE_STATE_MANAGER_REGISTRY_KEY_PATH,
                     "StateStore_RandomString")
  );

  /// <summary>
  /// Gets the names of all MTGO installation subpaths.
  /// </summary>
  /// <returns>
  /// An array of strings containing the names of all MTGO installations.
  /// </returns>
  public static string[]? GetInstallations()
  {
    var keyPath = @$"{SIDEBYSIDE_STATE_MANAGER_REGISTRY_KEY_PATH}\Applications";
    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, false))
    {
      if (key != null)
      {
        return key.GetSubKeyNames()
          .Where(k => k.StartsWith("mtgo..tion_"))
          .ToArray();
      }
    }

    return null;
  }

  /// <summary>
  /// Get the registry token from the specified key path and value name.
  /// </summary>
  /// <param name="keyPath">The registry key path.</param>
  /// <param name="valueName">The registry value name.</param>
  /// <returns>A string containing the registry token.</returns>
  private static string? GetRegistryToken(string keyPath, string valueName)
  {
    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, false))
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
