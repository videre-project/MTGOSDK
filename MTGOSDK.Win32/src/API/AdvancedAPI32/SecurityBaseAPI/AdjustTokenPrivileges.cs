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
  /// Enables or disables privileges in the specified access token.
  /// </summary>
  /// <remarks>
  /// Enabling or disabling privileges in an access token requires
  /// <c>TOKEN_ADJUST_PRIVILEGES</c> access.
  /// </remarks>
  [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
  public static extern bool AdjustTokenPrivileges(
    /// <summary>
    /// A handle to the access token that contains the privileges to be modified.
    /// </summary>
    /// <remarks>
    /// The handle must have <c>TOKEN_ADJUST_PRIVILEGES</c> access to the token.
    /// If the <c>PreviousState</c> parameter is not <c>IntPtr.Zero</c>, the
    /// handle must also have <c>TOKEN_QUERY</c> access.
    /// </remarks>
    IntPtr TokenHandle,
    /// <summary>
    /// Specifies whether the function disables all of the token's privileges.
    /// </summary>
    /// <remarks>
    /// If this value is <c>true</c>, the function disables all privileges in
    /// and ignores the <c>NewState</c> parameter. If it is <c>false</c>, the
    /// function modifies privileges based on the information pointed to by the
    /// <c>NewState</c> parameter.
    /// </remarks>
    bool DisableAllPrivileges,
    /// <summary>
    /// A pointer to a <c>TokenPrivileges</c> structure that specifies an array
    /// of privileges and their attributes.
    /// </summary>
    /// <remarks>
    /// If the <c>DisableAllPrivileges</c> parameter is <c>false</c>, the
    /// function modifies the privileges for the token.
    /// </remarks>
    ref TokenPrivileges NewState,
    /// <summary>
    /// Specifies the size, in bytes, of the buffer pointed to by the
    /// <c>PreviousState</c> parameter.
    /// </summary>
    /// <remarks>
    /// This parameter can be zero if the previous state of the privileges is
    /// <c>IntPtr.Zero</c>.
    /// </remarks>
    int BufferLength,
    /// <summary>
    /// A pointer to a buffer that the function fills with a
    /// <c>TokenPrivileges</c> structure that contains the previous state of any
    /// privileges that the function modifies.
    /// </summary>
    /// <remarks>
    /// If a privilege has been modified by this function, the privilege and its
    /// previous state are contained in the <c>TokenPrivileges</c> structure
    /// referenced by this parameter. If the <c>PrivilegeCount</c> member of the
    /// <c>TokenPrivileges</c> structure is zero, then no privileges have been
    /// changed by this function. This parameter can also be <c>IntPtr.Zero</c>.
    /// </remarks>
    IntPtr PreviousState,
    /// <summary>
    /// A pointer to a variable that receives the required size, in bytes, of
    /// the buffer pointed to by the <c>PreviousState</c> parameter.
    /// </summary>
    /// <remarks>
    /// This parameter can be <c>IntPtr.Zero</c> if the <c>PreviousState</c>
    /// parameter is <c>IntPtr.Zero</c>.
    /// </remarks>
    IntPtr ReturnLength
  );
}
