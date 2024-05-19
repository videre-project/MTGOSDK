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
  Terminate               = 0x0001,
  CreateThread            = 0x0002,
  VirtualMemoryOperation  = 0x0008,
  VirtualMemoryRead       = 0x0010,
  VirtualMemoryWrite      = 0x0020,
  DuplicateHandle         = 0x0040,
  /// <summary>
  /// Required to use this process as the parent process with
  /// PROC_THREAD_ATTRIBUTE_PARENT_PROCESS.
  /// </summary>
  CreateProcess           = 0x0080,
  SetQuota                = 0x0100,
  SetInformation          = 0x0200,
  QueryInformation        = 0x0400,
  /// <summary>
  /// Required to retrieve certain information about a process
  /// such as its token, exit code, and priority class.
  QueryLimitedInformation = 0x1000,
  /// <summary>
  /// Required to wait for the process to terminate using the wait functions.
  /// </summary>
  Synchronize             = 0x0010_0000,

  //All = 0x001F_0FFF,
  All = Terminate | CreateThread | 0x0004 | VirtualMemoryOperation
        | VirtualMemoryRead | VirtualMemoryWrite | DuplicateHandle | CreateProcess
        | SetQuota | SetInformation | QueryInformation | 0x0800
        | 0x001F_0000,
}
