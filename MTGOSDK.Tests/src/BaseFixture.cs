/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using NUnit.Framework.Interfaces;


namespace MTGOSDK.Tests;

[TestFixture]
[Parallelizable]
public abstract class BaseFixture : SetupFixture.Shared
{
  private static bool s_stop = false;

  public static void Write(string message) =>
    TestContext.WriteLine(message);

  private static void Mark(string? name = null) =>
    Write("----------------------- " + (name != null ? name + ":" : ""));

  [SetUp]
  public void Setup()
  {
    if (s_stop)
    {
      Assert.Inconclusive("Previous test failed");
    }
    else
    {
      Mark(TestContext.CurrentContext.Test.FullName);
    }
  }

  [TearDown]
  public void Cleanup()
  {
    if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
    {
      s_stop = true;
    }
    else if (!s_stop)
    {
      Mark();
    }
  }
}
