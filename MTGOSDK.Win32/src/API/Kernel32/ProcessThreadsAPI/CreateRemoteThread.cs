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
  /// Creates a thread that runs in the virtual address space of another process.
  /// </summary>
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr CreateRemoteThread(
    /// <summary>
    /// A handle to the process in which the thread is to be created.
    /// </summary>
    /// <remarks>
    /// The handle must have the PROCESS_CREATE_THREAD, PROCESS_QUERY_INFORMATION,
    /// PROCESS_VM_OPERATION, PROCESS_VM_WRITE, and PROCESS_VM_READ access rights.
    /// </remarks>
    [In] IntPtr hProcess,
    /// <summary>
    /// A pointer to a SECURITY_ATTRIBUTES structure that specifies a security
    /// descriptor for the new thread and determines whether child processes
    /// can inherit the returned handle.
    /// </summary>
    [In] IntPtr lpThreadAttributes,
    /// <summary>
    /// The size of the stack, in bytes.
    /// </summary>
    /// <remarks>
    /// The system rounds this value to the nearest page.
    /// If this parameter is 0 (zero), the new thread uses the default size for
    /// the executable.
    /// </remarks>
    [In] nuint dwStackSize,
    /// <summary>
    /// A pointer to the application-defined function to be executed by the thread.
    /// </summary>
		[In] IntPtr lpStartAddress,
    /// <summary>
    /// A pointer to a variable to be passed to the thread.
    /// </summary>
    [In] IntPtr lpParameter,
    /// <summary>
    /// The flags that control the creation of the thread.
    /// </summary>
    [In] CreationFlags dwCreationFlags,
    /// <summary>
    /// A pointer to a variable that receives the thread identifier.
    /// </summary>
    [Out] IntPtr lpThreadId
  );
}
