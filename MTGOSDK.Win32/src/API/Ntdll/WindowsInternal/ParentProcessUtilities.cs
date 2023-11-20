/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

/// <summary>
/// A utility class to determine a process parent.
/// </summary>
/// <remarks>
/// This struct layout implements the PROCESS_BASIC_INFORMATION struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct ParentProcessUtilities            // PROCESS_BASIC_INFORMATION
{
  internal IntPtr Reserved1;                    // NTSTATUS   ExitStatus
  internal IntPtr PebBaseAddress;               // PPEB
  internal IntPtr Reserved2_0;                  // ULONG_PTR  AffinityMask;
  internal IntPtr Reserved2_1;                  // KPRIORITY  BasePriority;
  internal IntPtr UniqueProcessId;              // ULONG_PTR
  internal IntPtr InheritedFromUniqueProcessId; // ULONG_PTR
}
