/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class User32
{
  /// <summary>
  /// Brings the thread that created the specified window into the foreground
  /// and activates the window.
  /// </summary>
  /// <returns>
  /// Returns <c>true</c> if the window was brought to the foreground; otherwise,
  /// <c>false</c>.
  /// </returns>
  /// <remarks>
  /// Keyboard input is directed to the window, and various visual cues are
  /// changed for the user. The system assigns a slightly higher priority to the
  /// thread that created the foreground window than it does to other threads.
  /// </remarks>
  [DllImport("user32.dll", SetLastError = true)]
  public static extern bool SetForegroundWindow(
    /// <summary>
    /// A handle to the window that should be activated and brought to the
    /// foreground.
    /// </summary>
    IntPtr hWnd
  );
}
