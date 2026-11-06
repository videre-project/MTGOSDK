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
  /// Releases or decommits memory allocated in a specified process.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is nonzero.
  /// If the function fails, the return value is zero. To get extended error
  /// information, call <see cref="GetLastError"/>.
  /// </returns>
  /// <remarks>
  /// Refer to https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualfreeex
  /// for more information.
  /// </remarks>
  [DllImport("kernel32", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool VirtualFreeEx(
    /// <summary>
    /// A handle to the process in which memory is to be freed.
    /// </summary>
    /// <remarks>
    /// The handle must have the PROCESS_VM_OPERATION access right.
    /// </remarks>
    [In] IntPtr hProcess,
    /// <summary>
    /// The address of the region of memory to free.
    /// </summary>
    /// <remarks>
    /// If dwFreeType is MEM_RELEASE, this must be the base address returned by
    /// <see cref="VirtualAllocEx"/>.
    /// </remarks>
    [In] IntPtr lpAddress,
    /// <summary>
    /// The size of the region of memory to free, in bytes.
    /// </summary>
    /// <remarks>
    /// If dwFreeType is MEM_RELEASE, this value must be zero.
    /// </remarks>
    [In] nuint dwSize,
    /// <summary>
    /// The type of free operation, specified by MEM_RELEASE or MEM_DECOMMIT.
    /// </summary>
    [In] uint dwFreeType
  );
}
