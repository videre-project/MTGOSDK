/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

using MTGOSDK.NUnit.Extensions;


namespace MTGOSDK.NUnit.Attributes;

/// <summary>
/// Specifies stack trace filter patterns to be applied at the assembly level.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ExceptionFilterAttribute(params string[] filterPatterns)
  : NUnitAttribute, IApplyToTest, IWrapSetUpTearDown, IWrapTestMethod
{
  /// <summary>
  /// Gets the collection of filter patterns used for stack trace filtering.
  /// /// </summary>
  public string[] FilterPatterns { get; } = filterPatterns;

  private readonly StackFilter _exceptionFilter = new(filterPatterns);

  /// <summary>
  /// Wrap a command and return the result.
  /// </summary>
  /// <param name="command">The command to be wrapped</param>
  /// <returns>The wrapped command</returns>
  public TestCommand Wrap(TestCommand command) =>
    new ExceptionFilterCommand(command, _exceptionFilter);

  /// <summary>
  /// Apply exception filter attribute on existing test methods
  /// </summary>
  /// <param name="test"></param>
  public void ApplyToTest(Test test)
  {
    // Get all test methods of all fixtures recursively
    var testMethodEnumerable = test is TestMethod testMethod
      ? testMethod.GetAllTestMethods()
      : test.GetAllTestFixtures()
            .SelectMany(f => f.GetAllTestMethods());

    // Wrap all test methods with the exception filter attribute and command.
    foreach (var method in testMethodEnumerable)
    {
      method.WrapWithAttributes(this);
    }
  }
}
