/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.Win32.API;

/// <summary>
/// Specifies the parts of the process or processes to be included in a snapshot.
/// </summary>
[Flags]
public enum SnapshotFlags : uint
{
  /// <summary>
  /// Includes all heaps of the process specified in th32ProcessID in the snapshot.
  /// </summary>
  HeapList  = 0x00000001, // TH32CS_SNAPHEAPLIST
  /// <summary>
  /// Includes all processes in the system in the snapshot.
  /// </summary>
  Process   = 0x00000002, // TH32CS_SNAPPROCESS
  /// <summary>
  /// Includes all threads in the system in the snapshot.
  /// </summary>
  Thread    = 0x00000004, // TH32CS_SNAPTHREAD
  /// <summary>
  /// Includes all modules of the process specified in th32ProcessID in the snapshot.
  /// </summary>
  Module    = 0x00000008, // TH32CS_SNAPMODULE
  /// <summary>
  /// Includes all 32-bit modules of the process specified in th32ProcessID in
  /// the snapshot when called from a 64-bit process.
  /// </summary>
  /// <remarks>
  /// This flag can be combined with the <see cref="Module"/>  or <see cref="All"/> flags.
  /// If the function fails with ERROR_BAD_LENGTH, retry the function until it succeeds.
  /// </remarks>
  Module32  = 0x00000010, // TH32CS_SNAPMODULE32
  /// <summary>
  /// Indicates that the snapshot handle is to be inheritable.
  /// </summary>
  Inherit   = 0x80000000, // TH32CS_INHERIT
  /// <summary>
  /// Includes all processes and threads in the system, plus the heaps and
  /// modules of the process specified in th32ProcessID.
  /// </summary>
  All       = 0x0000001F  // TH32CS_SNAPALL
}
