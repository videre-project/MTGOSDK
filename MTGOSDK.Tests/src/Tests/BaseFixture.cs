/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;

using NUnit.Framework.Internal;
using NUnit.Framework.Interfaces;


namespace MTGOSDK.Tests;

[TestFixture]
[Parallelizable]
public abstract class BaseFixture : Shared
{
  private static readonly string s_testResultsPath =
    Path.Combine(Directory.GetCurrentDirectory(), ".testresults");

  static BaseFixture()
  {
    // Delete the test results file if it already exists
    if (File.Exists(s_testResultsPath))
    {
      File.Delete(s_testResultsPath);
    }
  }

  internal void SetResult(TestExecutionContext context, int count)
  {
    this.Retries = count;
    if (count > 0) return;

    this.TestResult =
      context.CurrentResult.ResultState != ResultState.Failure &&
      context.CurrentResult.ResultState != ResultState.Error;
  }

  public string TestName;

  public bool? TestResult = null;

  public int Retries = 0;

  public static void Write(string message) =>
    TestContext.WriteLine(message);

  private static void Mark(string? message) =>
    Write("----------------------- " + message ?? "");

  [SetUp]
  public void Setup()
  {
    string className = this.GetType().Name;
    TestName = className + "." + TestContext.CurrentContext.Test.Name;

    Mark(TestName + ":");
    StartTime = DateTime.Now;
    TestResult = null;
  }

  [TearDown]
  public void Cleanup()
  {
    if (!TestResult.HasValue && Retries > 0) return;
    SetResult(TestExecutionContext.CurrentContext, 0);
    EndTime = DateTime.Now;

    // Append a new line to the test results file containing the test name and result
    string result = (TestResult!.Value ? "Success" : "Failure") + $" - Took {Duration.TotalSeconds:F2} seconds";
    File.AppendAllText(s_testResultsPath, $"{TestName}: {result}{Environment.NewLine}");

    Mark(result);
  }
}
