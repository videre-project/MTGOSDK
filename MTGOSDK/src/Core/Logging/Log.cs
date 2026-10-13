/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.Extensions.Logging;


namespace MTGOSDK.Core.Logging;

/// <summary>
/// Provides static ILogger extension methods for automated structured logging.
/// </summary>
public class Log : LoggerBase
{
  /// <summary>
  /// Suppresses all logging in the current context.
  /// </summary>
  public static IDisposable Suppress(LogLevel level = LogLevel.None) =>
    new SuppressionContext(level);

  /// <summary>
  /// Suppresses logging on the current thread only and does not flow to async continuations.
  /// Use this for transient retry/setup noise where background tasks must keep normal logging.
  /// </summary>
  public static IDisposable SuppressLocal(LogLevel level = LogLevel.None) =>
    new SuppressionContext(level, flowAcrossAsync: false);

  //
  // ILogger Extensions
  //

  public static void Trace(string? message, params object?[] args) =>
    GetLogger(LogLevel.Trace).LogTrace(message, args);
  public static void Trace(EventId eventId, string? message, params object?[] args) =>
    GetLogger(LogLevel.Trace).LogTrace(eventId, message, args);
  public static void Trace(Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Trace).LogTrace(exception, message, args);
  public static void Trace(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Trace).LogTrace(eventId, exception, message, args);

  public static void Debug(string? message, params object?[] args) =>
    GetLogger(LogLevel.Debug).LogDebug(message, args);
  public static void Debug(EventId eventId, string? message, params object?[] args) =>
    GetLogger(LogLevel.Debug).LogDebug(eventId, message, args);
  public static void Debug(Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Debug).LogDebug(exception, message, args);
  public static void Debug(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Debug).LogDebug(eventId, exception, message, args);

  public static void Information(string? message, params object?[] args) =>
    GetLogger(LogLevel.Information).LogInformation(message, args);
  public static void Information(EventId eventId, string? message, params object?[] args) =>
    GetLogger(LogLevel.Information).LogInformation(eventId, message, args);
  public static void Information(Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Information).LogInformation(exception, message, args);
  public static void Information(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Information).LogInformation(eventId, exception, message, args);

  public static void Warning(string? message, params object?[] args) =>
    GetLogger(LogLevel.Warning).LogWarning(message, args);
  public static void Warning(EventId eventId, string? message, params object?[] args) =>
    GetLogger(LogLevel.Warning).LogWarning(eventId, message, args);
  public static void Warning(Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Warning).LogWarning(exception, message, args);
  public static void Warning(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Warning).LogWarning(eventId, exception, message, args);

  public static void Error(string? message, params object?[] args) =>
    GetLogger(LogLevel.Error).LogError(message, args);
  public static void Error(EventId eventId, string? message, params object?[] args) =>
    GetLogger(LogLevel.Error).LogError(eventId, message, args);
  public static void Error(Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Error).LogError(exception, message, args);
  public static void Error(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Error).LogError(eventId, exception, message, args);

  public static void Critical(string? message, params object?[] args) =>
    GetLogger(LogLevel.Critical).LogCritical(message, args);
  public static void Critical(EventId eventId, string? message, params object?[] args) =>
    GetLogger(LogLevel.Critical).LogCritical(eventId, message, args);
  public static void Critical(Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Critical).LogCritical(exception, message, args);
  public static void Critical(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    GetLogger(LogLevel.Critical).LogCritical(eventId, exception, message, args);
}
