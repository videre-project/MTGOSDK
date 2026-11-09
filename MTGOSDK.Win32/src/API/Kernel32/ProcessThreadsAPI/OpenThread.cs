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
  /// Opens an existing thread object.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is an open handle to the
  /// specified thread. If the function fails, the return value is NULL.
  /// To get extended error information, call <see cref="GetLastError"/>.
  /// </returns>
  /// <remarks>
  /// When finished with the handle, close it using <see cref="CloseHandle"/>.
  /// <para>
  /// Refer to https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openthread
  /// for more information.
  /// </para>
  /// </remarks>
  [DllImport("kernel32", SetLastError = true)]
  public static extern IntPtr OpenThread(
    /// <summary>
    /// The access rights to request for the thread object.
    /// </summary>
    [In] uint dwDesiredAccess,
    /// <summary>
    /// Whether processes created by the calling process can inherit the handle.
    /// </summary>
    [In] bool bInheritHandle,
    /// <summary>
    /// The identifier of the thread to open.
    /// </summary>
    [In] uint dwThreadId
  );
}
