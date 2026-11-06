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
  /// Retrieves the context of the specified thread.
  /// </summary>
  /// <returns>
  /// If the function succeeds, the return value is nonzero.
  /// If the function fails, the return value is zero. To get extended error
  /// information, call <see cref="GetLastError"/>.
  /// </returns>
  /// <remarks>
  /// The caller must suspend the thread to obtain a valid context. Calling this
  /// for the current thread can succeed while returning an invalid context.
  /// <para>
  /// Refer to https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadcontext
  /// for more information.
  /// </para>
  /// </remarks>
  [DllImport("kernel32", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool GetThreadContext(
    /// <summary>
    /// A handle to the thread whose context is to be retrieved.
    /// </summary>
    /// <remarks>
    /// The handle must have the THREAD_GET_CONTEXT access right.
    /// </remarks>
    [In] IntPtr hThread,
    /// <summary>
    /// A pointer to a CONTEXT structure that receives the thread context.
    /// </summary>
    /// <remarks>
    /// ContextFlags selects the register groups to retrieve. The buffer must
    /// satisfy the alignment requirements for the thread architecture.
    /// </remarks>
    [In, Out] IntPtr lpContext
  );
}
