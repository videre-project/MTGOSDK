/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Takes a snapshot of the specified processes, as well as the heaps, modules,
  /// and threads used by these processes.
  /// </summary>
  /// <returns>
  /// If the function succeeds, it returns an open handle to the specified snapshot;
  /// otherwise, it returns INVALID_HANDLE_VALUE.
  /// </returns>
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern ToolHelpHandle CreateToolhelp32Snapshot(
    /// <summary>
    /// The portions of the system to be included in the snapshot.
    /// </summary>
    [In] SnapshotFlags dwFlags,
    /// <summary>
    /// The process identifier of the process to be included in the snapshot.
    /// This parameter can be zero to indicate the current process.
    /// </summary>
    [In] int th32ProcessID
  );
}
