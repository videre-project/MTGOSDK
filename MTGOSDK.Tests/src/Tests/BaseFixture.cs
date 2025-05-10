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
  private string _testResultsPath =>
    Path.Combine(Directory.GetCurrentDirectory(), ".testresults");

  public string TestName;

  public bool TestResult;

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
  }

  [TearDown]
  public void Cleanup()
  {
    TestResult = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Passed;
    EndTime = DateTime.Now;

    // Append a new line to the test results file containing the test name and result
    string result = (TestResult ? "Success" : "Failure") + $" - Took {Duration.TotalSeconds:F2} seconds";
    File.AppendAllText(_testResultsPath, $"{TestName}: {result}{Environment.NewLine}");

    Mark(result);
  }
}
