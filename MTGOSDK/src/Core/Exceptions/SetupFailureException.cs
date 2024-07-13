/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when the setup of the SDK fails.
/// </summary>
public class SetupFailureException : Exception
{
  public SetupFailureException() { }

  public SetupFailureException(string message)
    : base(message) { }

  public SetupFailureException(string message, Exception inner)
    : base(message, inner) { }
}
