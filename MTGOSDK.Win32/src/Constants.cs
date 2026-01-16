/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;

using MTGOSDK.Win32.Deployment;
using MTGOSDK.Win32.FileSystem;


namespace MTGOSDK.Win32;

/// <summary>
/// Provides global constants for the MTGO SDK.
/// </summary>
public static class Constants
{
  /// <summary>
  /// The Start Menu shortcut path for MTGO.
  /// </summary>
  public static string AppRefPath =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.Programs),
      "Daybreak Game Company LLC",
      "Magic The Gathering Online .appref-ms"
    );

  /// <summary>
  /// The MTGO application manifest uri for ClickOnce deployment.
  /// </summary>
  public static string ApplicationUri =
    "http://mtgo.patch.daybreakgames.com/patch/mtg/live/client/MTGO.application";

  /// <summary>
  /// The current application directory for MTGO.
  /// </summary>
  /// <remarks>
  /// Returns null if MTGO has never been installed.
  /// </remarks>
  public static string? MTGOAppDirectory =>
    ClickOncePaths.ApplicationDirectory is string appDir
      ? new Glob(@$"{appDir}\mtgo..tion_*")
      : null;

  /// <summary>
  /// The current data directory for MTGO's user data.
  /// </summary>
  /// <remarks>
  /// Returns null if MTGO has never been installed.
  /// </remarks>
  public static string? MTGODataDirectory =>
    ClickOncePaths.ApplicationDataDirectory is string dataDir
      ? new Glob(@$"{dataDir}\mtgo..tion_*\Data")
      : null;
}
