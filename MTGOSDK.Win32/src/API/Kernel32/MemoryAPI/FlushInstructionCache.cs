/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Flushes the instruction cache for the specified process.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is nonzero.
  /// If the function fails, the return value is zero. To get extended error
  /// information, call <see cref="GetLastError"/>.
  /// </returns>
  /// <remarks>
  /// Refer to https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-flushinstructioncache
  /// for more information.
  /// </remarks>
  [DllImport("kernel32", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool FlushInstructionCache(
    /// <summary>
    /// A handle to the process whose instruction cache is to be flushed.
    /// </summary>
    [In] IntPtr hProcess,
    /// <summary>
    /// A pointer to the base address of the region to be flushed.
    /// </summary>
    /// <remarks>
    /// If this parameter is NULL, the function flushes the entire process.
    /// </remarks>
    [In] IntPtr lpBaseAddress,
    /// <summary>
    /// The size of the region to be flushed, in bytes.
    /// </summary>
    /// <remarks>
    /// This parameter is ignored if lpBaseAddress is NULL.
    /// </remarks>
    [In] nuint dwSize
  );
}
