/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;


namespace MTGOSDK.Win32;

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
    // + "#" + string.Join(", ", new string[] {
    //   "MTGO.application",
    //   "Culture=neutral",
    //   "PublicKeyToken=dbac2845cba5280e",
    //   "processorArchitecture=msil"
    // });
}
