/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// The test command for the <see cref="RetryAttribute"/>
/// </summary>
public class RetryOnErrorCommand(TestCommand innerCommand, int tryCount)
    : DelegatingTestCommand(innerCommand)
{
  /// <summary>
  /// Runs the test, saving a TestResult in the supplied TestExecutionContext.
  /// </summary>
  /// <param name="context">The context in which the test should run.</param>
  /// <returns>A TestResult</returns>
  public override TestResult Execute(TestExecutionContext context)
  {
    int count = tryCount;
    while (count-- > 0)
    {
      try
      {
        context.CurrentResult = innerCommand.Execute(context);
      }
      // Commands are supposed to catch exceptions, but some don't
      // and we want to look at restructuring the API in the future.
      catch (Exception ex)
      {
        if (context.CurrentResult == null)
        {
          context.CurrentResult = context.CurrentTest.MakeTestResult();
        }
        context.CurrentResult.RecordException(ex);
      }

      if (context.CurrentResult.ResultState != ResultState.Failure &&
          context.CurrentResult.ResultState != ResultState.Error)
      {
        break;
      }

      // Clear result for retry
      if (count > 0)
      {
        context.CurrentResult = context.CurrentTest.MakeTestResult();
        context.CurrentRepeatCount++;
      }
    }

    return context.CurrentResult;
  }
}
