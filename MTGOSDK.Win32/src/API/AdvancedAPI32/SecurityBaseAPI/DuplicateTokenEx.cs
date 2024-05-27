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
  /// Creates a new access token that duplicates an existing token.
  /// </summary>
  /// <remarks>
  /// This function can create either a primary token or an impersonation token.
  /// </remarks>
  [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
  public static extern bool DuplicateTokenEx(
    /// <summary>
    /// A handle to an access token opened with <c>TOKEN_DUPLICATE</c> access.
    /// </summary>
    IntPtr hExistingToken,
    /// <summary>
    /// Specifies the requested access rights for the new token.
    /// </summary>
    /// <remarks>
    /// The <c>DuplicateTokenEx</c> function compares the requested access rights
    /// with the existing token's discretionary access control list (DACL) to
    /// determine which rights are granted or denied. To request the same access
    /// rights as the existing token, specify zero. To request all access rights
    /// that are valid for the caller, specify <c>MAXIMUM_ALLOWED</c>.
    /// </remarks>
    uint dwDesiredAccess,
    /// <summary>
    /// A pointer to a <c>SECURITY_ATTRIBUTES</c> structure that specifies a
    /// security descriptor for the new token and determines whether child
    /// processes can inherit the token.
    /// </summary>
    /// <remarks>
    /// If <c>lpTokenAttributes</c> is <c>IntPtr.Zero</c>, the token gets a
    /// default security descriptor and the handle cannot be inherited. If the
    /// security descriptor contains a system access control list (SACL), the
    /// token gets <c>ACCESS_SYSTEM_SECURITY</c> access right, even if it was
    /// not requested in <c>dwDesiredAccess</c>.
    /// <para/>
    /// To set the other in the security descriptor for the new token, the
    /// caller's process token must have the <c>SE_RESTORE_NAME</c> privilege.
    /// </remarks>
    IntPtr lpTokenAttributes,
    /// <summary>
    /// Specifies a value from the <c>SecurityImpersonationLevel</c> enum
    /// that indicates the impersonation level of the new token.
    /// </summary>
    SecurityImpersonationLevel impersonationLevel,
    /// <summary>
    /// Specifies a value from the <c>TokenType</c> enum that indicates the
    /// type of the new token.
    /// </summary>
    TokenType tokenType,
    /// <summary>
    /// A pointer to a <c>IntPtr</c> variable that receives the new token handle.
    /// </summary>
    /// <remarks>
    /// When you have finished using the new token, call the <c>CloseHandle</c>
    /// function to close the token handle.
    /// </remarks>
    out IntPtr phNewToken
  );
}
