/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Exceptions;

/// <summary>
/// Exception thrown when an external error occurs.
/// </summary>
public class ProcessCrashedException : Exception
{
  public int ProcessId;

  public ProcessCrashedException() { }

  public ProcessCrashedException(string message, int processId)
    : base(message)
  {
    this.ProcessId = processId;
  }

  public ProcessCrashedException(string message, int processId, Exception inner)
    : base(message, inner)
  {
    this.ProcessId = processId;
  }
}
