/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public struct TokenPrivileges
{
  public uint PrivilegeCount;

  [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
  public LUIDAndAttributes[] Privileges;
}
