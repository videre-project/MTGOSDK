/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using MTGOSDK.Tests;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// The test command for the <see cref="RetryAttribute"/>
/// </summary>
public class RetryOnErrorCommand(
  TestCommand innerCommand,
  int tryCount,
  RetryBehavior retryBehavior)
    : DelegatingTestCommand(innerCommand)
{
  private void SetbaseFixtureResult(TestExecutionContext context, int count)
  {
    object? fixture = context.CurrentTest.Fixture;
    if (fixture is BaseFixture baseFixture)
    {
      baseFixture.SetResult(context, count);
    }
  }

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
      finally
      {
        SetbaseFixtureResult(context, count);
      }

      if (context.CurrentResult.ResultState != ResultState.Failure &&
          context.CurrentResult.ResultState != ResultState.Error)
      {
        if (retryBehavior == RetryBehavior.UntilPasses) break;
      }
      else if (retryBehavior == RetryBehavior.UntilFails) break;

      // Clear result for retry
      if (count > 0)
      {
        context.CurrentResult = context.CurrentTest.MakeTestResult();
        context.CurrentRepeatCount++;
      }
    }
    SetbaseFixtureResult(context, 0);

    return context.CurrentResult;
  }
}
