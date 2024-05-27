/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class AdvApi32
{
  /// <summary>
  /// Opens the access token associated with a process.
  /// </summary>
  [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
  public static extern bool OpenProcessToken(
    /// <summary>
    /// A handle to the process whose access token is opened.
    /// </summary>
    /// <remarks>
    /// The process must have the <c>PROCESS_QUERY_INFORMATION</c> access right.
    /// </remarks>
    IntPtr ProcessHandle,
    /// <summary>
    /// Specifies an access mask that specifies the requested types of access to
    /// the access token.
    /// </summary>
    /// <remarks>
    /// These requested access types are compared with the discretionary access
    /// control list (DACL) of the token to determine which accesses are granted
    /// or denied.
    /// </remarks>
    int DesiredAccess,
    /// <summary>
    /// A pointer to a handle that identifies the newly opened access token when
    /// the function returns.
    /// </summary>
    ref IntPtr TokenHandle
  );
}
