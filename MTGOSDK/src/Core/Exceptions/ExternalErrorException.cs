/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when an external error occurs.
/// </summary>
public class ExternalErrorException : Exception
{
    public ExternalErrorException() { }

    public ExternalErrorException(string message)
        : base(message) { }

    public ExternalErrorException(string message, Exception inner)
        : base(message, inner) { }
}
