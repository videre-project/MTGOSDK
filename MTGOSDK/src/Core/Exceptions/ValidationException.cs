/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when a validation error occurs.
/// </summary>
public class ValidationException : Exception
{
  public ValidationException() { }

  public ValidationException(string message)
    : base(message) { }

  public ValidationException(string message, Exception inner)
    : base(message, inner) { }
}
