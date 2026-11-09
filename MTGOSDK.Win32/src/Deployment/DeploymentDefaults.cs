/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;


namespace MTGOSDK.Win32.Deployment;

internal static class DeploymentDefaults
{
  internal const string ApplicationUri =
    "http://mtgo.patch.daybreakgames.com/patch/mtg/live/client/MTGO.application";

  internal static string AppRefPath =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.Programs),
      "Daybreak Game Company LLC",
      "Magic The Gathering Online .appref-ms"
    );
}
