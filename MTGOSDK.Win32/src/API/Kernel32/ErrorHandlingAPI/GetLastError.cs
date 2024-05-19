/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Retrieves the caller thread's last-error code value.
  /// </summary>
  /// <returns>
  /// The return value is the calling thread's last-error code value.
  /// </returns>
  /// <remarks>
  /// The last-error code is maintained on a per-thread basis.
  /// Multiple threads do not overwrite each other's last-error code.
  /// </remarks>s
  [DllImport("kernel32.dll")]
  public static extern int GetLastError();
}
