/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

[StructLayout(LayoutKind.Sequential)]
public struct LUID
{
  public readonly uint LowPart;
  public readonly int HighPart;
}
