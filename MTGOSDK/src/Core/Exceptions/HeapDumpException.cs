/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when an external error occurs.
/// </summary>
public class HeapDumpException : Exception
{
  public HeapDumpException() { }

  public HeapDumpException(string message)
    : base(message) { }

  public HeapDumpException(string message, Exception inner)
    : base(message, inner) { }
}
