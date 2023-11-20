/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Closes an open object handle.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is nonzero.
  /// If the function fails, the return value is zero.
  /// </returns>
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool CloseHandle(
    /// <summary>
    /// A valid handle to an open object.
    /// </summary>
    [In] IntPtr hHandle
  );
}
