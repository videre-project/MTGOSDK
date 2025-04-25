/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// The test command for the <see cref="RetryAttribute"/>
/// </summary>
public class ExceptionFilterCommand(
  TestCommand innerCommand,
  StackFilter exceptionFilter)
    : DelegatingTestCommand(innerCommand)
{
  /// <summary>
  /// Runs the test, saving a TestResult in the supplied TestExecutionContext.
  /// </summary>
  /// <param name="context">The context in which the test should run.</param>
  /// <returns>A TestResult</returns>
  public override TestResult Execute(TestExecutionContext context)
  {
    try
    {
      context.CurrentResult = innerCommand.Execute(context);
    }
    catch (Exception ex)
    {
      context.CurrentResult ??= context.CurrentTest.MakeTestResult();
      context.CurrentResult.RecordException(ex);
    }
    finally
    {
      exceptionFilter.Filter(context);
    }

    return context.CurrentResult;
  }
}
