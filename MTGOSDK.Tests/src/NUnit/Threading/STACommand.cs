/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;

using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

using MTGOSDK.Core.Exceptions;


namespace MTGOSDK.NUnit.Threading;

public class STACommand(TestCommand command) : TestCommand(command.Test)
{
  public virtual TestResult RunCommand(TestExecutionContext context)
  {
    return command.Execute(context);
  }

  public override TestResult Execute(TestExecutionContext context)
  {
    TestResult? result = null;
    var thread = new Thread(() => result = RunCommand(context));
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    return result
      ?? throw new ExternalErrorException("Failed to run test in STA!");
  }
}
