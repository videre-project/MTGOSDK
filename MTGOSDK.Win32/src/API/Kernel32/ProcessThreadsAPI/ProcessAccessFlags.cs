/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.Win32.API;

/// <summary>
/// Specifies access rights to a process object.
/// </summary>
[Flags]
public enum ProcessAccessFlags : uint
{
  All                     = 0x001F0FFF,
  Terminate               = 0x00000001,
  CreateThread            = 0x00000002,
  VirtualMemoryOperation  = 0x00000008,
  VirtualMemoryRead       = 0x00000010,
  VirtualMemoryWrite      = 0x00000020,
  DuplicateHandle         = 0x00000040,
  /// <summary>
  /// Required to use this process as the parent process with
  /// PROC_THREAD_ATTRIBUTE_PARENT_PROCESS.
  /// </summary>
  CreateProcess           = 0x00000080,
  SetQuota                = 0x00000100,
  SetInformation          = 0x00000200,
  QueryInformation        = 0x00000400,
  /// <summary>
  /// Required to retrieve certain information about a process
  /// such as its token, exit code, and priority class.
  QueryLimitedInformation = 0x00001000,
  /// <summary>
  /// Required to wait for the process to terminate using the wait functions.
  /// </summary>
  Synchronize             = 0x00100000,
}
