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
  /// Retrieves the identifier of the thread that created the specified window
  /// and, optionally, the identifier of the process that created the window.
  /// </summary>
  [DllImport("user32.dll", SetLastError = true)]
  public static extern uint GetWindowThreadProcessId(
    /// <summary>
    /// A handle to the window.
    /// </summary>
    IntPtr hWnd,
    /// <summary>
    /// A pointer to a variable that receives the identifier of the thread that
    /// created the window.
    /// </summary>
    /// <remarks>
    /// If this parameter is not <c>null</c>, <c>GetWindowThreadProcessId</c>
    /// copies the identifier of the thread that created the window to the
    /// variable; otherwise, it does not. If the function fails, the value of
    /// the variable is unchanged.
    /// </remarks>
    out uint lpdwProcessId
  );
}
