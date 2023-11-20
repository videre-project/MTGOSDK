/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Retrieves information about the next module associated with a process or
  /// thread.
  /// </summary>
  /// <returns>
  /// Returns TRUE if the next entry of the module list has been copied to the
  /// buffer or FALSE otherwise.
  /// </returns>
  /// <remarks>
  /// To retrieve information about the first module associated with a process,
  /// use the <cref="Module32First"/> function.
  /// </remarks>
  [DllImport("kernel32.dll")]
  public static extern bool Module32Next(
    /// <summary>
    /// A handle to the snapshot returned from a previous call to the
    /// <cref="CreateToolhelp32Snapshot"/> function.
    /// </summary>
    [In] ToolHelpHandle hSnapshot,
    /// <summary>
    /// A pointer to a <cref="MODULEENTRY32"/> structure.
    /// </summary>
    [In, Out] ref MODULEENTRY32 lpme
  );
}
