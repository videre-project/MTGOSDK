/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class User32
{
  /// <summary>
  /// Retrieves the window handle to the Shell's desktop window.
  /// </summary>
  [DllImport("user32.dll")]
  public static extern IntPtr GetShellWindow();
}
