/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct StartupInfo
{
  public readonly int cb;
  public readonly string lpReserved;
  public readonly string lpDesktop;
  public readonly string lpTitle;
  public readonly int dwX;
  public readonly int dwY;
  public readonly int dwXSize;
  public readonly int dwYSize;
  public readonly int dwXCountChars;
  public readonly int dwYCountChars;
  public readonly int dwFillAttribute;
  public int dwFlags;
  public short wShowWindow;
  public readonly short cbReserved2;
  public readonly IntPtr lpReserved2;
  public readonly IntPtr hStdInput;
  public readonly IntPtr hStdOutput;
  public readonly IntPtr hStdError;
}
