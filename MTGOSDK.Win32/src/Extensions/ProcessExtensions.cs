/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Win32.Extensions;

/// <summary>
/// Provides extension methods for the <see cref="Process"/> class.
/// </summary>
public static class ProcessExtensions
{
  /// <summary>
  /// Gets the parent process of the current process.
  /// </summary>
  /// <param name="process">The process to query.</param>
  /// <returns>An instance of the Process class.</returns>
  public static Process? GetParentProcess(this Process process)
  {
    IntPtr handle = process.Handle;
    ParentProcessUtilities pbi = new ParentProcessUtilities();
    int status = Ntdll.NtQueryInformationProcess(
      handle,
      0,
      ref pbi,
      Marshal.SizeOf(pbi),
      out int returnLength
    );
    if (status != 0)
      throw new Win32Exception(status);

    int parentId = pbi.InheritedFromUniqueProcessId.ToInt32();
    try { return Process.GetProcessById(parentId); } catch { }

    return null;
  }

  /// <summary>
  /// Enumerates the modules loaded by the specified process.
  /// </summary>
  /// <param name="process">The process to query.</param>
  /// <returns>
  /// An enumerable collection of ModuleEntry32 structures.
  /// </returns>
  public static IEnumerable<ModuleEntry32> GetModules(this Process process)
  {
    var me32 = default(ModuleEntry32);
    var hModuleSnap = Kernel32.CreateToolhelp32Snapshot(
      SnapshotFlags.Module | SnapshotFlags.Module32,
      process.Id
    );

    if (hModuleSnap.IsInvalid)
    {
      yield break;
    }

    using (hModuleSnap)
    {
      me32.dwSize = (uint)Marshal.SizeOf(me32);

      if (Kernel32.Module32First(hModuleSnap, ref me32))
      {
        do { yield return me32; }
        while (Kernel32.Module32Next(hModuleSnap, ref me32));
      }
    }
  }

  /// <summary>
  /// Converts a <see cref="ProcessModuleCollection"/> to an enumerable collection of <see cref="ProcessModule"/>.
  /// </summary>
  /// <param name="collection">The <see cref="ProcessModuleCollection"/> to convert.</param>
  /// <returns>An enumerable collection of <see cref="ProcessModule"/>.</returns>
  public static IEnumerable<ProcessModule> AsEnumerable(this ProcessModuleCollection collection)
  {
    foreach (ProcessModule module in collection)
    {
      yield return module;
    }
  }

  /// <summary>
  /// Determines whether the specified process is a 64-bit process.
  /// </summary>
  /// <param name="process">The process to query.</param>
  /// <returns>True if the process is 64-bit; otherwise, false.</returns>
  /// <exception cref="Win32Exception">
  /// Thrown when failing to query the process bitness via native methods.
  /// </exception>
  /// <remarks>
  /// Adapted from https://stackoverflow.com/a/33206186/4075549 by user626528.
  /// </remarks>
  public static bool Is64Bit(this Process process)
  {
    if (!Environment.Is64BitOperatingSystem)
      return false;

    if (!Kernel32.IsWow64Process(process.Handle, out bool isWow64))
      throw new Win32Exception(
          "Failed to determine whether the process is running under WOW64.");

    return !isWow64;
  }
}
