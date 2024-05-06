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
  /// Frees a snapshot of the address space of a process.
  /// </summary>
  [DllImport("kernel32")] // SetLastError=true ?
  public static extern int PssFreeSnapshot(
    /// <summary>
    /// A handle to the process that contains the snapshot.
    /// </summary>
    /// <remarks>
    /// The handle must have the PROCESS_VM_READ, PROCESS_VM_OPERATION,
    /// and PROCESS_DUP_HANDLE rights. If the snapshot was captured from the
    /// current process, or duplicated into the current process, then pass in
    /// the result of <see cref="GetCurrentProcess"/>.
    /// </remarks>
    [In] IntPtr ProcessHandle,
    /// <summary>
    /// A handle to the snapshot to be freed.
    /// </summary>
    [In] IntPtr SnapshotHandle
  );
}
