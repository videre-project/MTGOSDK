/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

using MTGOSDK.Win32.Utilities;


namespace MTGOSDK.Win32.Deployment;

/// <summary>
/// Provides utility methods for interoping with ClickOnce deployment.
/// </summary>
public static class ClickOncePaths
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
  /// The path to the ClickOnce service executable.
  /// </summary>
  /// <remarks>
  /// This is the executable that is used to launch and install ClickOnce
  /// applications. It handles redirects from .application and .appref-ms files.
  /// </remarks>
  public static string ClickOnceServiceExecutable =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                 @"Microsoft.NET\Framework\v4.0.30319\dfsvc.exe");

  /// <summary>
  /// The application directory for ClickOnce deployments.
  /// </summary>
  public static string ApplicationDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    @"Apps\2.0",
    RegistryStore.GetRegistryToken(SIDEBYSIDE_REGISTRY_KEY_PATH,
                     "ComponentStore_RandomString")
  );

  /// <summary>
  /// The application data directory for ClickOnce user data.
  /// </summary>
  public static string ApplicationDataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    @"Apps\2.0\Data",
    RegistryStore.GetRegistryToken(SIDEBYSIDE_STATE_MANAGER_REGISTRY_KEY_PATH,
                     "StateStore_RandomString")
  );

  /// <summary>
  /// Gets the names of all MTGO installation subpaths.
  /// </summary>
  /// <param name="manifestName">
  /// The filename of the ClickOnce manifest.
  /// </param>
  /// <returns>
  /// An array of strings containing the names of all MTGO installations.
  /// </returns>
  public static string[]? GetInstallationNames(string manifestName)
  {
    string manifestKey = manifestName[..4] + ".." + manifestName[^4..] + "_";
    var keyPath = @$"{SIDEBYSIDE_STATE_MANAGER_REGISTRY_KEY_PATH}\Applications";
    using (RegistryKey? key = RegistryStore.GetUserRegistryKey(keyPath))
    {
      if (key != null)
      {
        return key.GetSubKeyNames()
          .Where(k => k.StartsWith(manifestKey.ToLower()))
          .ToArray();
      }
    }

    return null;
  }
}
