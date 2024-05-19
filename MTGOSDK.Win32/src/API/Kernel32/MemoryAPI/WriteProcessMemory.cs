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
  /// Writes data to an area of memory in a specified process.
  /// </summary>
  /// <remarks>
  /// The entire area to be written to must be accessible or the operation fails.
  /// </remarks>
  [DllImport("kernel32", SetLastError=true)]
  public static extern bool WriteProcessMemory(
    /// <summary>
    /// A handle to the process memory to be modified.
    /// </summary>
    /// <remarks>
    /// The handle must have PROCESS_VM_WRITE and PROCESS_VM_OPERATION access to
    /// the process.
    /// </remarks>
    [In] IntPtr hProcess,
    /// <summary>
    /// A pointer to the base address in the specified process to which data is
    /// written.
    /// </summary>
    /// <remarks>
    /// Before data transfer occurs, the system verifies that all data in the base
    /// address and memory of the specified size is accessible for write access,
    /// and if it is not accessible, the function fails.
    /// </remarks>
    [In] IntPtr lpBaseAddress,
    /// <summary>
    /// A pointer to the buffer that contains data to be written in the address
    /// space of the specified process.
    /// </summary>
    [In, Out] byte[] lpBuffer,
    /// <summary>
    /// The number of bytes to be written to the specified process.
    /// </summary>
		[In] nuint nSize,
    /// <summary>
    /// The number of bytes written to the specified process.
    /// </summary>
    out nuint lpNumberOfBytesWritten
  );
}
