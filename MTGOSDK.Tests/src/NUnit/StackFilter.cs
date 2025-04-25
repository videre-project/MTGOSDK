/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;


namespace MTGOSDK.NUnit;

public class StackFilter(IEnumerable<string> filterPatterns)
{
  private readonly IEnumerable<Regex> _regexPatterns =
    filterPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase));

  private static StackFilter? GetStackFilter(Test? test = null)
  {
    Assembly assembly;
    if (test != null)
    {
      IMethodInfo? testMethod = test.Method;
      if (testMethod == null) return null;

      Type? declaringType = testMethod.MethodInfo.DeclaringType;
      if (declaringType == null) return null;

      assembly = declaringType.Assembly;
    }
    else
    {
      assembly = Assembly.GetCallingAssembly();
    }

    // Check if the assembly has the ExceptionFilterAttribute
    var filterAttribute = assembly.GetCustomAttribute<ExceptionFilterAttribute>();
    if (filterAttribute == null) return null;

    return new(filterAttribute.FilterPatterns);
  }

  public static void FilterException(TestExecutionContext context)
  {
    // Modify the stack trace to filter out internal stack frames
    if ((context.CurrentResult.ResultState == ResultState.Failure ||
         context.CurrentResult.ResultState == ResultState.Error) &&
        GetStackFilter(context.CurrentTest) is StackFilter exceptionFilter)
    {
      context.CurrentResult.SetResult(
        context.CurrentResult.ResultState,
        context.CurrentResult.Message,
        // exceptionFilter.Filter(context.CurrentResult.Message)!,
        exceptionFilter.Filter(context.CurrentResult.StackTrace)
      );
    }
  }

  public void Filter(TestExecutionContext context)
  {
    // Modify the stack trace to filter out internal stack frames
    if (context.CurrentResult.ResultState == ResultState.Failure ||
        context.CurrentResult.ResultState == ResultState.Error)
    {
      context.CurrentResult.SetResult(
        context.CurrentResult.ResultState,
        context.CurrentResult.Message,
        // exceptionFilter.Filter(context.CurrentResult.Message)!,
        Filter(context.CurrentResult.StackTrace)
      );
    }
  }

  public string? Filter(string? rawTrace)
  {
    if (rawTrace is null) return null;

    StringReader sr = new(rawTrace);
    StringWriter sw = new();

    IEnumerable<Regex> patterns = _regexPatterns;
    bool positiveMatch = false;

    // On assertion failure, we only care about the frames that point to tests.
    if (rawTrace.Contains("NUnit.Framework.Assert.That[TActual]"))
    {
      // Instead include only lines that match the test assembly name.
      string assemblyName = typeof(StackFilter).Assembly.GetName().Name!;
      patterns = patterns.Append(new Regex(assemblyName));
      positiveMatch = true;
    }

    // Filter out all lines that match any of our filter patterns.
    string? line;
    while ((line = sr.ReadLine()) != null)
    {
      if (patterns.Any(regex => regex.IsMatch(line)) == positiveMatch)
      {
        sw.WriteLine(line);
      }
    }

    return sw.ToString();
  }
}
