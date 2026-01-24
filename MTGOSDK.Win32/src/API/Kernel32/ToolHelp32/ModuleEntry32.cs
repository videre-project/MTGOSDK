/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

/// <summary>
/// Describes an entry from a list of the modules belonging to the specified
/// process.
/// </summary>
/// <remarks>
/// The modBaseAddr and hModule members are valid only in the context of the
/// process specified byth32ProcessID.
/// </remarks>
[DebuggerDisplay("{" + nameof(szModule) + "}")]
[StructLayout(LayoutKind.Sequential)]
public struct ModuleEntry32
{
  /// <summary>
  /// The size of the structure, in bytes.
  /// </summary>
  /// <remarks>
  /// Before calling the <see cref="Module32First"/> function, set this member to
  /// sizeof(<see cref="ModuleEntry32"/>). If you do not initialize
  /// <see cref="dwSize"/>, <see cref="Module32First"/> fails.
  /// </remarks>
  public uint dwSize;
  /// <summary>
  /// This member is no longer used, and is always set to one.
  /// </summary>
  public uint th32ModuleID;
  /// <summary>
  /// The identifier of the process whose modules are to be examined.
  /// </summary>
  public uint th32ProcessID;
  /// <summary>
  /// The load count of the module, which is not generally meaningful, and
  /// usually equal to 0xFFFF.
  /// </summary>
  public uint GlblcntUsage;
  /// <summary>
  /// The load count of the module (same as <see cref="GlblcntUsage"/>), which is not
  /// generally meaningful, and usually equal to 0xFFFF.
  /// </summary>
  public uint ProccntUsage;
  /// <summary>
  /// The base address of the module in the context of the owning process.
  /// </summary>
  public readonly IntPtr modBaseAddr;
  /// <summary>
  /// The size of the module, in bytes.
  /// </summary>
  public uint modBaseSize;
  /// <summary>
  /// A handle to the module in the context of the owning process.
  /// </summary>
  public readonly IntPtr hModule;

  /// <summary>
  /// The module name.
  /// </summary>
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
  public string szModule;

  /// <summary>
  /// The module path.
  /// </summary>
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
  public string szExePath;
}
