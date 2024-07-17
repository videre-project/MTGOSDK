/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MTGOSDK.Core.Compiler.Extensions;
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
  private static ILoggerProvider s_provider = NullLoggerProvider.Instance;

  /// <summary>
  /// Represents a type used to configure the logging system and create
  /// instances of <see cref="ILogger"/>.
  /// </summary>
  private static ILoggerFactory s_factory = NullLoggerFactory.Instance;
  private static readonly ILogger s_nulllogger = s_factory.CreateLogger("NullLogger");

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
      // If logging is suppressed and the caller type is a suppressed type,
      // return a null logger to prevent logging from suppressed sources.
      if (SuppressionContext.IsSuppressedCallerType())
        return s_nulllogger;

      // Get the caller type of the calling method or class.
      Type callerType;
      int depth = 3;
      do { callerType = GetCallerType(depth); depth++; }
      while (callerType.FullName.StartsWith("System.") ||
             callerType.FullName.StartsWith("MTGOSDK.Core.Logging."));

      // Fetch the base type if the caller is a compiler-generated type
      // (e.g. lambda expressions, async state machines, etc.).
      if (!s_loggers.ContainsKey(callerType) && callerType.IsCompilerGenerated())
      {
        if (!s_callerTypes.TryGetValue(callerType, out Type? baseType))
          baseType = callerType.GetBaseType();

        callerType = baseType;
      }

      return s_loggers.GetOrAdd(callerType, CreateLogger(callerType));
    }
  }

  /// <summary>
  /// Creates a logger instance for the given caller type.
  /// </summary>
  private static ILogger CreateLogger(Type callerType)
  {
    if (s_provider != null)
      return s_provider.CreateLogger(callerType.FullName);
    if (s_factory != null)
      return s_factory.CreateLogger(callerType);

    throw new InvalidOperationException(
        "No logger provider or factory has been set.");
  }

  /// <summary>
  /// Sets the logger provider instance to be used.
  /// </summary>
  public static void SetProviderInstance(ILoggerProvider provider)
  {
    if (provider != s_provider)
      s_loggers.Clear();
    s_provider = provider;
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
