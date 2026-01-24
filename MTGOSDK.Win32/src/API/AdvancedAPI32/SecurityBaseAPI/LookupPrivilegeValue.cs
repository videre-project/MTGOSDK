/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Runtime.InteropServices;


namespace MTGOSDK.Win32.API;

public static partial class AdvApi32
{
  /// <summary>
  /// Retrieves the locally unique identifier (LUID) used on a specified system
  /// to locally represent the specified privilege name.
  /// </summary>
  [DllImport("advapi32.dll", SetLastError = true)]
  public static extern bool LookupPrivilegeValue(
    /// <summary>
    /// The name of the system on which the privilege name is retrieved.
    /// </summary>
    /// <remarks>
    /// If this parameter is <c>null</c>, the function attempts to find the
    /// privilege name on the local system.
    /// </remarks>
    string? lpSystemName,
    /// <summary>
    /// The name of the privilege, as defined in the system (<c>Winnt.h</c> header file).
    /// </summary>
    /// <remarks>
    /// For example, to specify the constant <c>SE_SECURITY_NAME</c>, use it's
    /// corresponding string "SeSecurityPrivilege".
    /// </remarks>
    string lpName,
    /// <summary>
    /// A pointer to a variable that receives the LUID by which the privilege is
    /// known on the system specified by the <c>lpSystemName</c> parameter.
    /// </summary>
    ref LUID lpLuid
  );
}
