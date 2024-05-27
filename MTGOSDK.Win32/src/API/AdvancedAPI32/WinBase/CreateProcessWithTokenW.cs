/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class AdvApi32
{
  [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  public static extern bool CreateProcessWithTokenW(
    IntPtr hToken,
    int dwLogonFlags,
    string lpApplicationName,
    string lpCommandLine,
    int dwCreationFlags,
    IntPtr lpEnvironment,
    string lpCurrentDirectory,
    [In] ref StartupInfo lpStartupInfo,
    out ProcessInformation lpProcessInformation
  );
}
