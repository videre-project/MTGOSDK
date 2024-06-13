/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;


namespace MTGOSDK.Tests;

[TestFixture]
[Parallelizable]
public abstract class BaseFixture : SetupFixture.Shared
{
  public static void Write(string message) =>
    TestContext.WriteLine(message);

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
