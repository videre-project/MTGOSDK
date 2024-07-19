/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Security;
using System.Runtime.InteropServices;

using MTGOSDK.Core.Remoting;


namespace MTGOSDK.Core.Security;

/// <summary>
/// Provides extension methods for <c>System.Security</c> classes.
/// </summary>
public static class SecurityExtensions
{
  /// <summary>
  /// Securely transfers a SecureString object to the remote client.
  /// </summary>
  /// <param name="password">The SecureString object to transfer.</param>
  /// <returns>
  /// A handle to the remote SecureString object (this cannot be read remotely).
  /// </returns>
  public static dynamic RemoteSecureString(this SecureString password)
  {
    IntPtr passwordPtr = IntPtr.Zero;
    char @char;
    // Create a new SecureString object on the remote client.
    dynamic secure_pwd = RemoteClient.CreateInstance(new TypeProxy<SecureString>());
    try
    {
      // Allocate a global handle for the password string in unmanaged memory.
      passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(password);
      // Extract each unicode character to build the remote SecureString object.
      for (int i=0; i < password.Length; i++) {
        short unicodeChar = Marshal.ReadInt16(passwordPtr, i * 2);
        @char = Convert.ToChar(unicodeChar);
        secure_pwd.AppendChar(@char);
      }
    }
    finally
    {
      // Cleanup the last character extracted.
      @char = default(char);
      // Free the unmanaged memory used by the password string.
      Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
    }

    return secure_pwd;
  }
}
