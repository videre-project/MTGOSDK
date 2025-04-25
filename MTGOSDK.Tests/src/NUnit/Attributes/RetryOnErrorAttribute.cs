/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

using MTGOSDK.NUnit.Extensions;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// Applies retry functionality to the test assembly, fixture, or test method,
/// allowing it to be reattempted a specified number of times.
/// </summary>
/// <remarks>
/// The retry behavior is hierarchical and can be overridden at lower levels.
/// If the test assembly, fixture, or test method has the <see cref="NoRetryAttribute" />,
/// retry functionality is ignored.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly |
                AttributeTargets.Class |
                AttributeTargets.Method, AllowMultiple=false, Inherited=true)]
public class RetryOnErrorAttribute(
  int retryCount,
  RetryBehavior retryBehavior = RetryBehavior.UntilPasses)
    : NUnitAttribute, IRepeatTest, IApplyToTest
{
  private readonly int _tryCount = retryCount + 1;

  /// <summary>
  /// Wrap a command and return the result.
  /// </summary>
  /// <param name="command">The command to be wrapped</param>
  /// <returns>The wrapped command</returns>
  public TestCommand Wrap(TestCommand command) =>
    new RetryOnErrorCommand(command, _tryCount, retryBehavior);

  /// <summary>
  /// Apply retry attribute on existing test methods
  /// </summary>
  /// <param name="test"></param>
  public void ApplyToTest(Test test)
  {
    // Get all test methods of all fixtures recursively
    var testMethodEnumerable = test is TestMethod testMethod
      ? testMethod.GetRetryableTestCasesRecursively()
      : test.GetRetryableTestMethodsRecursively();

    // Add retry attribute and replace the test method with the wrapped one
    foreach (var method in testMethodEnumerable)
    {
      method.WrapWithAttributes(new RetryOnErrorAttribute(retryCount, retryBehavior));
    }
  }
}
