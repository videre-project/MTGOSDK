/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Determines whether the specified process is running under WOW64.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is a nonzero value. If the
  /// function fails, the return value is zero. To get extended error information,
  /// call GetLastError.
  /// </returns>
  /// <remarks>
  /// Refer to https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-iswow64process
  /// for more information.
  /// </remarks>
  [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool IsWow64Process(
    /// <summary>
    /// A handle to the process.
    /// </summary>
    /// <remarks>
    /// The handle must have the PROCESS_QUERY_INFORMATION or
    /// PROCESS_QUERY_LIMITED_INFORMATION access right.
    /// </remarks>
    [In] IntPtr process,
    /// <summary>
    /// A pointer to a value that is set to TRUE if the process is running under
    /// WOW64 on an Intel64 or x64 processor. If the process is running under
    /// 32-bit Windows, the value is set to FALSE.
    /// </summary>
    [Out] out bool wow64Process
  );
}
