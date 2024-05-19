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
  /// Reserves, commits, or changes the state of a region of memory within the
  /// virtual address space of a specified process.
  /// </summary>
  /// </remarks>
  /// The function initializes the memory it allocates to zero (unless MEM_RESET
  /// is used).
  /// </remarks>
  [DllImport("kernel32", SetLastError=true)]
  public static extern IntPtr VirtualAllocEx(
    /// <summary>
    /// A handle to the process in which memory is to be allocated.
    /// </summary>
    /// <remarks>
    /// The function allocates memory within the virtual address space of this
    /// process. The handle must have the PROCESS_VM_OPERATION access right.
    /// </remarks>
    IntPtr hProcess,
    /// <summary>
    /// The address of the region of memory to allocate.
    /// </summary>
    IntPtr lpAddress,
    /// <summary>
    /// The size of the region of memory to allocate, in bytes.
    /// </summary>
    uint dwSize,
    /// <summary>
    /// The type of memory allocation.
    /// </summary>
    int flAllocationType,
    /// <summary>
    /// The type of memory protection for the region of pages to be allocated.
    /// </summary>
		int flProtect
  );
}
