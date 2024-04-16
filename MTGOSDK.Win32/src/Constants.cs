/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;

using MTGOSDK.Win32.Utilities;
using MTGOSDK.Win32.Utilities.FileSystem;


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
  public static string MTGOAppDirectory =>
    new Glob(@$"{DeploymentUtilities.ApplicationDirectory}\mtgo..tion_*");

  /// <summary>
  /// The current data directory for MTGO's user data.
  /// </summary>
  public static string MTGODataDirectory =>
    new Glob(@$"{DeploymentUtilities.ApplicationDataDirectory}\mtgo..tion_*\Data");
}
