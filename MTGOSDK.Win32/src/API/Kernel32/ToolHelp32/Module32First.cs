/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Kernel32
{
  /// <summary>
  /// Retrieves information about the first module associated with a process.
  /// </summary>
  /// <returns>
  /// Returns TRUE if the first entry of the module list has been copied to the
  /// buffer or FALSE otherwise.
  /// </returns>
  /// <remarks>
  /// The calling application must set the <see cref="ModuleEntry32.dwSize"/> member
  /// of <paramref name="lpme"/> to the size, in bytes, of the structure.
  /// </remarks>
  [DllImport("kernel32.dll")]
  public static extern bool Module32First(
    /// <summary>
    /// A handle to the snapshot returned from a previous call to the
    /// <see cref="CreateToolhelp32Snapshot"/> function.
    /// </summary>
    [In] ToolHelpHandle hSnapshot,
    /// <summary>
    /// A pointer to a <see cref="ModuleEntry32"/> structure.
    /// </summary>
    [In, Out] ref ModuleEntry32 lpme
  );
}
