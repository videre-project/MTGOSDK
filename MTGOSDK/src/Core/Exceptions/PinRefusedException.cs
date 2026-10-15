/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// A pin reference was refused because the client it would be counted for is
/// not registered: it never registered, or it left or exited. No reference
/// was taken, so the client never holds one it could not return.
/// </summary>
public class PinRefusedException(string msg) : InvalidOperationException(msg)
{
  public PinRefusedException(int registration)
    : this($"Client registration {registration} is unknown or has been removed; no pin was taken.")
  {
  }
}
