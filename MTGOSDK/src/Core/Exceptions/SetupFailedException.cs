/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when the setup of the SDK fails.
/// </summary>
public class SetupFailedException : Exception
{
    public SetupFailedException() { }

    public SetupFailedException(string message)
        : base(message) { }

    public SetupFailedException(string message, Exception inner)
        : base(message, inner) { }
}
