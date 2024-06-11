/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;

using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

using MTGOSDK.Core.Exceptions;

using MTGOSDK.NUnit.Threading;


namespace MTGOSDK.NUnit;

/// <summary>
/// This attribute rate limits the execution of an NUnit test method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class RateLimitAttribute(int ms = 100) : STATestAttribute
{
  public override TestCommand GetSTACommand(TestCommand command) =>
    new RateLimitCommand(command, ms);

  private class RateLimitCommand(TestCommand command, int ms)
      : STACommand(command)
  {
    public static readonly object s_lock = new();

    public override TestResult RunCommand(TestExecutionContext context)
    {
      lock (s_lock)
      {
        Thread.Sleep(ms);
        return base.RunCommand(context);
      }
    }
  }
}
