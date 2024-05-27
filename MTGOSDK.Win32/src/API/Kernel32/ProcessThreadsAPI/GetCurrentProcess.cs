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
  /// Retrieves a pseudo handle for the current process.
  /// </summary>
  [DllImport("kernel32.dll", ExactSpelling = true)]
  public static extern IntPtr GetCurrentProcess();
}
