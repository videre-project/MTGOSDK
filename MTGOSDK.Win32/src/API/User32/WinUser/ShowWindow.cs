/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class User32
{
  /// <summary>
  /// Sets the specified window's show state.
  /// </summary>
  [DllImport("user32.dll")]
  public static extern bool ShowWindow(
    /// <summary>
    /// A handle to the window.
    /// </summary>
    [In] IntPtr hWnd,
    /// <summary>
    /// Controls how the window is to be shown.
    /// </summary>
    /// <remarks>
    /// This parameter is ignored the first time an application calls ShowWindow,
    /// if the program that launched the application provides a STARTUPINFO
    /// structure. Otherwise, the first time ShowWindow is called, the value
    /// should be the value obtained by the WinMain function in its nCmdShow
    /// parameter.
    /// </remarks>
    [In] ShowWindowFlags nCmdShow
  );
}
