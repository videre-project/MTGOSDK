/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MTGOSDK.Core.Reflection;


namespace MTGOSDK.Core.Logging;

/// <summary>
/// Wraps an ILogger instance to provide custom logging functionality.
/// </summary>
public class LoggerBase : DLRWrapper<ILoggerFactory>, ILogger
{
  /// <summary>
  /// Represents a type used to configure the logging system and create
  /// instances of <see cref="ILogger"/>.
  /// </summary>
  private static ILoggerFactory s_factory = NullLoggerFactory.Instance;

  /// <summary>
  /// A concurrent dictionary of ILogger<T> instances mapped to their type.
  /// </summary>
  /// <remarks>
  /// Used to cache ILogger instances for performance.
  /// </remarks>
  private static readonly ConcurrentDictionary<Type, ILogger> s_loggers = new();

  /// <summary>
  /// A concurrent dictionary of caller types mapped to their base types.
  /// </summary>
  /// <remarks>
  /// Used to map compiler-generated types within a class to their base types.
  /// </remarks>
  private static readonly ConcurrentDictionary<Type, Type> s_callerTypes = new();

  /// <summary>
  /// Represents a type used to perform logging.
  /// </summary>
  /// <remarks>Aggregates most logging patterns to a single method.</remarks>
  internal static ILogger s_logger
  {
    get
    {
      Type callerType = GetCallerType(3);

      // Fetch the base type if the caller is a compiler-generated type
      // (e.g. lambda expressions, async state machines, etc.).
      if (!s_loggers.ContainsKey(callerType) && IsCompilerGenerated(callerType))
      {
        if (!s_callerTypes.TryGetValue(callerType, out Type? baseType))
        {
          string fullName = callerType.FullName;
          string baseName = fullName.Substring(0, fullName.IndexOf("+<"));
          baseType = callerType.DeclaringType.Assembly.GetType(baseName);
          s_callerTypes.TryAdd(callerType, baseType);
        }
        callerType = baseType;
      }

      return s_loggers.GetOrAdd(callerType, s_factory.CreateLogger(callerType));
    }
  }

  /// <summary>
  /// Sets the logger factory instance to be used.
  /// </summary>
  public static void SetFactoryInstance(ILoggerFactory factory)
  {
    if (factory != s_factory)
      s_loggers.Clear();
    s_factory = factory;
  }

  /// <summary>
  /// Writes a log entry.
  /// </summary>
  /// <param name="logLevel">Entry will be written on this level.</param>
  /// <param name="eventId">Id of the event.</param>
  /// <param name="state">The entry to be written. Can be also an object.</param>
  /// <param name="exception">The exception related to this entry.</param>
  /// <param name="formatter">Function to create a <see cref="string"/> message of the <paramref name="state"/> and <paramref name="exception"/>.</param>
  /// <typeparam name="TState">The type of the object to be written.</typeparam>
  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter
  ) =>
    s_logger.Log(logLevel, eventId, state, exception, formatter);

  /// <summary>
  /// Checks if the given <paramref name="logLevel"/> is enabled.
  /// </summary>
  /// <param name="logLevel">Level to be checked.</param>
  /// <returns><c>true</c> if enabled.</returns>
  public bool IsEnabled(LogLevel logLevel) =>
    s_logger.IsEnabled(logLevel);

  /// <summary>
  /// Begins a logical operation scope.
  /// </summary>
  /// <param name="state">The identifier for the scope.</param>
  /// <typeparam name="TState">The type of the state to begin scope for.</typeparam>
  /// <returns>An <see cref="IDisposable"/> that ends the logical operation scope on dispose.</returns>
  public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
    s_logger.BeginScope(state);
}
