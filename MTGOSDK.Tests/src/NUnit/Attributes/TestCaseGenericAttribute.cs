/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Builders;


namespace MTGOSDK.NUnit.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class TestCaseGenericAttribute(params object[] arguments)
    : TestCaseAttribute(arguments), ITestBuilder
{
  public Type[] TypeArguments { get; set; } = null!;

  IEnumerable<TestMethod> ITestBuilder.BuildFrom(IMethodInfo method, Test? suite)
  {
    if (!method.IsGenericMethodDefinition)
      return base.BuildFrom(method, suite);

    if (TypeArguments == null || TypeArguments.Length != method.GetGenericArguments().Length)
    {
      var parms = new TestCaseParameters { RunState = RunState.NotRunnable };
      parms.Properties.Set(PropertyNames.SkipReason,
          $"{nameof(TypeArguments)} should have {method.GetGenericArguments().Length} elements");
      return new[] { new NUnitTestCaseBuilder().BuildTestMethod(method, suite, parms) };
    }

    var genMethod = method.MakeGenericMethod(TypeArguments);
    return base.BuildFrom(genMethod, suite);
  }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class TestCaseGenericAttribute<T> : TestCaseGenericAttribute
{
  public TestCaseGenericAttribute(params object[] arguments) : base(arguments) =>
    TypeArguments = new[] { typeof(T) };
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class TestCaseGenericAttribute<T1, T2> : TestCaseGenericAttribute
{
  public TestCaseGenericAttribute(params object[] arguments) : base(arguments) =>
    TypeArguments = new[] { typeof(T1), typeof(T2) };
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class TestCaseGenericAttribute<T1, T2, T3> : TestCaseGenericAttribute
{
  public TestCaseGenericAttribute(params object[] arguments) : base(arguments) =>
    TypeArguments = new[] { typeof(T1), typeof(T2), typeof(T3) };
}
