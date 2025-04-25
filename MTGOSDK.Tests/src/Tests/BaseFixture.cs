/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using NUnit.Framework.Internal;


namespace MTGOSDK.Tests;

[TestFixture]
[Parallelizable]
public abstract class BaseFixture : Shared
{
  public static void Write(string message) =>
    TestContext.WriteLine(message);

  private static void Mark(string? message) =>
    Write("----------------------- " + message ?? "");

  [SetUp]
  public void Setup()
  {
    string className = this.GetType().Name;
    string testName = TestContext.CurrentContext.Test.Name;

    Mark(className + "." + testName + ":");
    StartTime = DateTime.Now;
  }

  [TearDown]
  public void Cleanup()
  {
    EndTime = DateTime.Now;
    Mark($"Took {Duration.TotalSeconds:F2} seconds");
  }
}
