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
  /// Opens an existing local process object.
  /// </summary>
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr OpenProcess(
    /// <summary>
    /// The access to the process object.
    /// </summary>
    /// <remarks>
    /// This access right is checked against the security descriptor for the
    /// process. This parameter acn be one or more of the process access rights.
    /// <para>
    /// If the caller has enabled the SeDebugPrivilege privilege, the requested
    /// access is granted regardless of the contents of the security descriptor.
    /// </para>
    /// </remarks>
    [In] ProcessAccessFlags dwDesiredAccess,
    /// <summary>
    /// If this value is TRUE, processes created by this process will inherit
    /// the handle. Otherwise, the processes do not inherit this handle.
    /// </summary>
    [In] bool bInheritHandle,
    /// <summary>
    /// The identifier of the local process to be opened.
    /// </summary>
    /// <remarks>
    /// If the specified process is the System Process (0x00000000), the function
    /// fails and the last error code is ERROR_INVALID_PARAMETER. If the specified
    /// process is the Idle process or one of the CSRSS processes, this function
    /// fails and the last error code is ERROR_ACCESS_DENIED because their access
    /// restictions prevent user-level code from opening them.
    /// </remarks>
		[In] uint dwProcessId
  );
}
