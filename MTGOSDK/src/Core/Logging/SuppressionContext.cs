/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;


using MTGOSDK.Core.Compiler.Extensions;


namespace MTGOSDK.Core.Logging;

/// <summary>
/// A class that suppresses logging for a specific caller type.
/// </summary>
/// <remarks>
/// This class creates a context that suppresses all logging invoked from the
/// caller type (and all functions called by the caller type) until the context
/// is disposed. This is useful for suppressing logging for a specific caller
/// type without having to modify the logging configuration or log level.
/// </remarks>
public class SuppressionContext : IDisposable
{
  private static Type baseType;

  /// <summary>
  /// A concurrent bag of caller types that have been suppressed from logging.
  /// </summary>
  private static readonly ConcurrentDictionary<Type, LogLevel> s_suppressedCallerTypes = new();

  public SuppressionContext(LogLevel logLevel = LogLevel.None)
  {
    // Get the type of the original caller creating the suppression context.
    Type callerType;
    int depth = 3;
    do { callerType = GetCallerType(depth); depth++; }
    while (callerType.FullName.StartsWith("System.") ||
          callerType.FullName.StartsWith("MTGOSDK.Core.Logging."));

    baseType = callerType.GetBaseType();
    s_suppressedCallerTypes.TryAdd(baseType, logLevel);
    Log.Debug("Suppressed logging for {type} below loglevel {level}", baseType, logLevel);
  }

  public static bool IsSuppressedCallerType()
  {
    // If there are no suppressed caller types, return false.
    if (s_suppressedCallerTypes.Count == 0)
      return false;

    // Search the entire call stack for a suppressed caller type.
    try
    {
      int depth = 3; // Default stack depth to begin searching.
      for (int i = depth; i < 50; i++)
      {
        Type callerType = GetCallerType(i);
        Type baseType = callerType.GetBaseType();
        if (s_suppressedCallerTypes.ContainsKey(callerType))
          return true;
      }
    }
    catch { /* Have fetched past the current depth of the call stack. */ }

    return false;
  }

  public void Dispose()
  {
    s_suppressedCallerTypes.TryRemove(baseType, out _);
  }
}
