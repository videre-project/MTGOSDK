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
  /// Reads data from an area of memory in a specified process.
  /// </summary>
  [DllImport("kernel32", SetLastError=true)]
  public static extern bool ReadProcessMemory(
    /// <summary>
    /// A handle to the process with memory that is being read.
    /// </summary>
    /// <remarks>
    /// The handle must have PROCESS_VM_READ access to the process.
    /// </remarks>
    [In] IntPtr hProcess,
    /// <summary>
    /// A pointer to the base address in the specified process from which to read.
    /// </summary>
    /// <remarks>
    /// Before any data transfer occurs, the system verifies that all data in
    /// the base address and memory of the specified size is accessible for read
    /// access, and if it is not accessible, the function fails.
    /// </remarks>
    [In] IntPtr lpBaseAddress,
    /// <summary>
    /// A pointer to a buffer that receives the contents from the address space
    /// of the specified process.
    /// </summary>
    [Out] byte[] lpBuffer,
    /// <summary>
    /// The number of bytes to be read from the specified process.
    /// </summary>
		[In] nuint nSize,
    /// <summary>
    /// The number of bytes read into the specified buffer.
    /// </summary>
    out nuint lpNumberOfBytesRead
  );
}
