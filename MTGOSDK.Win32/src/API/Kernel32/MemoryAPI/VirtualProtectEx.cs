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
  /// Changes the protection of committed pages in a specified process.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is nonzero.
  /// If the function fails, the return value is zero. To get extended error
  /// information, call <see cref="GetLastError"/>.
  /// </returns>
  /// <remarks>
  /// After writing executable code, the calling application must use
  /// <see cref="FlushInstructionCache"/> to ensure instruction cache coherency.
  /// <para>
  /// Refer to https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotectex
  /// for more information.
  /// </para>
  /// </remarks>
  [DllImport("kernel32", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool VirtualProtectEx(
    /// <summary>
    /// A handle to the process in which memory protection is to be changed.
    /// </summary>
    /// <remarks>
    /// The handle must have the PROCESS_VM_OPERATION access right.
    /// </remarks>
    [In] IntPtr hProcess,
    /// <summary>
    /// The address of the region of memory whose protection is to be changed.
    /// </summary>
    /// <remarks>
    /// All affected pages must belong to the same reserved allocation.
    /// </remarks>
    [In] IntPtr lpAddress,
    /// <summary>
    /// The size of the region of memory whose protection is to be changed,
    /// in bytes.
    /// </summary>
    /// <remarks>
    /// All pages containing one or more bytes in the specified range are affected.
    /// </remarks>
    [In] nuint dwSize,
    /// <summary>
    /// The memory protection to apply to the specified region.
    /// </summary>
    [In] uint flNewProtect,
    /// <summary>
    /// The previous access protection of the first page in the specified region.
    /// </summary>
    [Out] out uint lpflOldProtect
  );
}
