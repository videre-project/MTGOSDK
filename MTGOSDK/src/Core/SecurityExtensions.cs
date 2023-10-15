/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Security;
using System.Runtime.InteropServices;


namespace MTGOSDK.Core;

public static class SecurityExtensions
{
  /// <summary>
  /// Securely transfers a SecureString object to the remote client.
  /// </summary>
  /// <param name="password"></param>
  /// <returns></returns>
  public static dynamic RemoteSecureString(this SecureString password)
  {
    IntPtr passwordPtr = IntPtr.Zero;
    // Create a new SecureString object on the remote client.
    dynamic secure_pwd = RemoteClient.CreateInstance(new Proxy<SecureString>());
    try
    {
      // Allocate a global handle for the password string in unmanaged memory.
      passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(password);
      // Extract each unicode character to build the remote SecureString object.
      for (int i=0; i < password.Length; i++) {
        short unicodeChar = Marshal.ReadInt16(passwordPtr, i * 2);
        char @char = Convert.ToChar(unicodeChar);
        secure_pwd.AppendChar(@char);
      }
    }
    finally
    {
      // Free the unmanaged memory used by the password string.
      Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
    }

    return secure_pwd;
  }
}
