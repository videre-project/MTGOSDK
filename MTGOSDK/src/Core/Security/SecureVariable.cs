/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

#pragma warning disable CS8600, CS8604, CS8625 // Null checks not enforceable.

using System.Security;
using System.Runtime.InteropServices;


namespace MTGOSDK.Core.Security;

/// <summary>
/// A simple variable wrapper for secure credential storage.
/// </summary>
public struct SecureVariable(SecureString value)
{
  private SecureString Value { get; } = value;

  public static implicit operator SecureString(SecureVariable variable) =>
    variable.Value;

  public static implicit operator string(SecureVariable variable) =>
    variable.ToString();

  /// <summary>
  /// Converts a SecureVariable object from a SecureString object to a string.
  /// </summary>
  /// <returns>The string value of the SecureString object.</returns>
  /// <remarks>
  /// This method is not secure and should only be used for debugging purposes
  /// or when the SecureString object is not used to store sensitive data.
  /// </remarks>
  public override string ToString()
  {
    IntPtr valuePtr = IntPtr.Zero;
    try
    {
      valuePtr = Marshal.SecureStringToGlobalAllocUnicode(value);
      return Marshal.PtrToStringUni(valuePtr);
    }
    finally
    {
      Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
    }
  }
}
