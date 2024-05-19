/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.Win32.API;

/// <summary>
/// Specifies the parts of the process or processes to be included in a snapshot.
/// </summary>
[Flags]
public enum CreationFlags : uint
{
  /// <summary>
  /// The thread runs immediately after creation.
  /// </summary>
  NONE                             = 0x00000000,
  /// <summary>
  /// The thread is created in a suspended state, and does not run until the
  /// ResumeThread function is called.
  /// </summary>
  CREATE_SUSPENDED                  = 0x00000004,
  /// <summary>
  /// The dwStackSize parameter specifies the initial reserve size of the stack.
  /// </summary>
  /// <remarks>
  /// If this flag is not specified, dwStackSize specifies the commit size.
  /// </remarks>
  STACK_SIZE_PARAM_IS_A_RESERVATION = 0x00010000
}
