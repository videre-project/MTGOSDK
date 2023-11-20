/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class Ntdll
{
  /// <summary>
  /// Retrieves information about the specified process.
  /// </summary>
  /// <remarks>
  /// For more information, refer to
  /// https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntqueryinformationprocess
  /// </remarks>
  [DllImport("ntdll.dll")]
  public static extern int NtQueryInformationProcess(
    /// <summary>
    /// A handle to the process for which information is to be retrieved.
    /// </summary>
    [In] IntPtr processHandle,
    /// <summary>
    /// The type of process information to be retrieved.
    /// </summary>
    [In] int processInformationClass,
    /// <summary>
    /// A pointer to a buffer supplied by the calling application into which the
    /// function writes the requested information.
    /// </summary>
    [In, Out] ref ParentProcessUtilities processInformation,
    /// <summary>
    /// The byte size of the buffer pointed to by the processInformation parameter.
    /// </summary>
    [In] int processInformationLength,
    /// <summary>
    /// A pointer to a variable in which the function returns the size of the
    /// requested information.
    /// </summary>
    /// <remarks>
    /// If the function was successful, this is the size of the information
    /// written to the buffer pointed to by the processInformation parameter,
    /// but if the buffer was too small, this is the minimum size of buffer
    /// needed to receive the information successfully.
    /// </remarks>
    [Out, Optional] out int returnLength
  );
}
