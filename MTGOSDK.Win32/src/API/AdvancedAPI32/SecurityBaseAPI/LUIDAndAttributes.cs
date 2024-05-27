/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LUIDAndAttributes
{
  public LUID Luid;
  public uint Attributes;
}
