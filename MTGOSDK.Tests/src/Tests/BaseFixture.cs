/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Tests;

[TestFixture]
[Parallelizable]
public abstract class BaseFixture : Shared
{
  public static void Write(string message) =>
    TestContext.Out.WriteLine(message);

  private static void Mark(string? name = null) =>
    Write("----------------------- " + (name != null ? name + ":" : ""));

  [SetUp]
  public void Setup()
  {
    Mark(TestContext.CurrentContext.Test.FullName);
  }

  [TearDown]
  public void Cleanup()
  {
    Mark();
  }
}
