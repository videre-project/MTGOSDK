/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Win32.Utilities;

public static class ProcessUtilities
{
  public static Process? RunAsDesktopUser(string fileName, string arguments, bool hideWindow = false)
  {
    //
    // Enable SeIncreaseQuotaPrivilege in this process.
    //
    IntPtr hProcessToken = IntPtr.Zero;
    try
    {
      IntPtr process = Kernel32.GetCurrentProcess();
      if (!AdvApi32.OpenProcessToken(process, 0x0020, ref hProcessToken))
        return null;

      TokenPrivileges tkp = new()
      {
        PrivilegeCount = 1,
        Privileges = new LUIDAndAttributes[1]
      };

      if (!AdvApi32.LookupPrivilegeValue(null, "SeIncreaseQuotaPrivilege", ref tkp.Privileges[0].Luid))
        return null;

      tkp.Privileges[0].Attributes = 0x00000002;

      if (!AdvApi32.AdjustTokenPrivileges(hProcessToken, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero))
        return null;
    }
    finally
    {
      Kernel32.CloseHandle(hProcessToken);
    }

    //
    // Get an HWND representing the desktop shell.
    //
    // This will fail if the shell is not running (crashed or terminated), or
    // if the default shell has been replaced with a custom shell.
    //
    IntPtr hwnd = User32.GetShellWindow();
    if (hwnd == IntPtr.Zero)
      return null;

    IntPtr hShellProcess = IntPtr.Zero;
    IntPtr hShellProcessToken = IntPtr.Zero;
    IntPtr hPrimaryToken = IntPtr.Zero;
    try
    {
      // Get the PID of the desktop shell process.
      uint dwPID;
      if (User32.GetWindowThreadProcessId(hwnd, out dwPID) == 0)
        return null;

      // Open the desktop shell process in order to query it (get the token)
      hShellProcess = Kernel32.OpenProcess(ProcessAccessFlags.QueryInformation, false, dwPID);
      if (hShellProcess == IntPtr.Zero)
        return null;

      // Get the process token of the desktop shell.
      if (!AdvApi32.OpenProcessToken(hShellProcess, 0x0002, ref hShellProcessToken))
        return null;

      uint dwTokenRights = 395U;

      //
      // Duplicate the shell's process token to get a primary token.
      //
      // Based on experimentation, this is the minimal set of rights required
      // for CreateProcessWithTokenW (contrary to current documentation).
      //
      if (!AdvApi32.DuplicateTokenEx(
          hShellProcessToken,
          dwTokenRights,
          IntPtr.Zero,
          SecurityImpersonationLevel.SecurityImpersonation,
          TokenType.TokenPrimary,
          out hPrimaryToken
        ))
        return null;

      // Start the target process with the new token.
      StartupInfo si = new();
      if (hideWindow)
      {
        si.dwFlags = 0x00000001;
        si.wShowWindow = 0;
      }

      ProcessInformation pi = new();
      if (!AdvApi32.CreateProcessWithTokenW(
          hPrimaryToken,
          0,
          fileName,
          $"\"{fileName}\" {arguments}",
          0,
          IntPtr.Zero,
          Path.GetDirectoryName(fileName)!,
          ref si,
          out pi
        ))
      {
        // Get the last error and display it.
        int error = Marshal.GetLastWin32Error();
        return null;
      }

      return Process.GetProcessById(pi.dwProcessId);
    }
    finally
    {
      Kernel32.CloseHandle(hShellProcessToken);
      Kernel32.CloseHandle(hPrimaryToken);
      Kernel32.CloseHandle(hShellProcess);
    }
  }
}
