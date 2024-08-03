/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;


namespace MTGOSDK.NUnit.Extensions;

public static class TestExtensionMethods
{
  /// <summary>
  /// Replace the method property of a <see cref="TestMethod"/> with an extended attribute list
  /// </summary>
  /// <param name="method">The target <see cref="TestMethod"/></param>
  /// <param name="extraAttrs">List of attribute objects, .e.g. <see cref="RetryOnErrorAttribute"/></param>
  public static void WrapWithAttributes(this TestMethod method, params Attribute[] extraAttrs) =>
    method.Method = new CustomAttributeMethodWrapper(method.Method, extraAttrs);

  /// <summary>
  /// Find all the underlying test fixtures plus their underlying <see cref="TestMethod" />
  /// </summary>
  /// <param name="test">Input <see cref="Test" /></param>
  /// <returns></returns>
  public static IEnumerable<TestMethod> GetRetryableTestMethodsRecursively(this Test test) =>
    test
      .GetAllTestFixtures()
      .Where(fixture => !fixture.HasAnAttributeOf<NoRetryAttribute>())
      .SelectMany(fixture => fixture.GetAllTestMethods())
      .Where(method => !method.HasAnAttributeOf<NoRetryAttribute>());

  /// <summary>
  /// Find all the underlying test cases, aka. test methods under a <see cref="TestMethod" />
  /// </summary>
  /// <param name="test">Input <see cref="TestMethod" /></param>
  /// <returns></returns>
  public static IEnumerable<TestMethod> GetRetryableTestCasesRecursively(this TestMethod test) =>
    test.GetAllTestMethods()
      .Where(method => !method.HasAnAttributeOf<NoRetryAttribute>());

  private static IEnumerable<TestFixture> GetAllTestFixtures(this ITest test)
  {
    List<TestFixture> testMethods = [];

    if (test is TestFixture fixture)
      testMethods.Add(fixture);

    if (!test.Tests.Any()) return testMethods;

    foreach (var subTest in test.Tests)
    {
      testMethods.AddRange(GetAllTestFixtures((Test)subTest));
    }

    return testMethods;
  }

  private static IEnumerable<TestMethod> GetAllTestMethods(this ITest test)
  {
    List<TestMethod> testMethods = [];

    if (test is TestMethod testMethod)
      testMethods.Add(testMethod);

    if (!test.Tests.Any()) return testMethods;

    foreach (var subTest in test.Tests)
    {
      testMethods.AddRange(GetAllTestMethods((Test)subTest));
    }

    return testMethods;
  }

  private static bool HasAnAttributeOf<T>(this Test testMethod) where T : class =>
    testMethod.GetCustomAttributes<T>(true).Any();
}