/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;


namespace ScubaDiver;

internal class Logger
{
  #region P/Invoke Console Spawning

  [DllImport("kernel32.dll",
    EntryPoint = "GetStdHandle",
    SetLastError = true,
    CharSet = CharSet.Auto,
    CallingConvention = CallingConvention.StdCall)]
  private static extern IntPtr GetStdHandle(int nStdHandle);

  [DllImport("kernel32.dll",
    EntryPoint = "AllocConsole",
    SetLastError = true,
    CharSet = CharSet.Auto,
    CallingConvention = CallingConvention.StdCall)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool AllocConsole();

  private const int STD_OUTPUT_HANDLE = -11;

  #endregion

#if DEBUG
  public static bool IsDebug = true;
#else
  public static bool IsDebug = false;
#endif

  public static void RedirectConsole()
  {
    if (IsDebug && !Debugger.IsAttached && AllocConsole())
    {
      IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
      SafeFileHandle safeFileHandle = new(stdHandle, true);
      FileStream fileStream = new(safeFileHandle, FileAccess.Write);
      Encoding encoding = Encoding.ASCII;
      StreamWriter standardOutput = new(fileStream, encoding) { AutoFlush = true };
      Console.SetOut(standardOutput);
    }
  }

  internal static void Debug(string s)
  {
    if (IsDebug || Debugger.IsAttached)
    {
      System.Diagnostics.Debug.WriteLine(s);
    }
  }
}
