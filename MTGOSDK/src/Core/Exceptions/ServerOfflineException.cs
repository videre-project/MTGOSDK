/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when MTGO is under maintenance or is otherwise offline.
/// </summary>
public class ServerOfflineException : Exception
{
  public ServerOfflineException() { }

  public ServerOfflineException(string message)
    : base(message) { }

  public ServerOfflineException(string message, Exception inner)
    : base(message, inner) { }
}
