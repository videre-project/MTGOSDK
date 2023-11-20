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
  /// Frees the loaded dynamic-link library (DLL) module and, if necessary,
  /// decrements its reference count.
  /// </summary>
  [DllImport("kernel32", SetLastError=true)]
  public static extern bool FreeLibrary(
    /// <summary>
    /// A handle to the loaded library module.
    /// </summary>
    /// <remarks>
    /// The LoadLibrary, LoadLibraryEx, GetModuleHandle, or GetModuleHandleEx
    /// functions return this handle.
    /// </remarks>
    [In] IntPtr hModule
  );
}
