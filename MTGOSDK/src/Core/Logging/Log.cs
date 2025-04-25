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

  //
  // ILogger Extensions
  //

  public static void Trace(string? message, params object?[] args) =>
    s_logger.LogTrace(message, args);
  public static void Trace(EventId eventId, string? message, params object?[] args) =>
    s_logger.LogTrace(eventId, message, args);
  public static void Trace(Exception? exception, string? message, params object?[] args) =>
    s_logger.LogTrace(exception, message, args);
  public static void Trace(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    s_logger.LogTrace(eventId, exception, message, args);

  public static void Debug(string? message, params object?[] args) =>
    s_logger.LogDebug(message, args);
  public static void Debug(EventId eventId, string? message, params object?[] args) =>
    s_logger.LogDebug(eventId, message, args);
  public static void Debug(Exception? exception, string? message, params object?[] args) =>
    s_logger.LogDebug(exception, message, args);
  public static void Debug(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    s_logger.LogDebug(eventId, exception, message, args);

  public static void Information(string? message, params object?[] args) =>
    s_logger.LogInformation(message, args);
  public static void Information(EventId eventId, string? message, params object?[] args) =>
    s_logger.LogInformation(eventId, message, args);
  public static void Information(Exception? exception, string? message, params object?[] args) =>
    s_logger.LogInformation(exception, message, args);
  public static void Information(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    s_logger.LogInformation(eventId, exception, message, args);

  public static void Warning(string? message, params object?[] args) =>
    s_logger.LogWarning(message, args);
  public static void Warning(EventId eventId, string? message, params object?[] args) =>
    s_logger.LogWarning(eventId, message, args);
  public static void Warning(Exception? exception, string? message, params object?[] args) =>
    s_logger.LogWarning(exception, message, args);
  public static void Warning(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    s_logger.LogWarning(eventId, exception, message, args);

  public static void Error(string? message, params object?[] args) =>
    s_logger.LogError(message, args);
  public static void Error(EventId eventId, string? message, params object?[] args) =>
    s_logger.LogError(eventId, message, args);
  public static void Error(Exception? exception, string? message, params object?[] args) =>
    s_logger.LogError(exception, message, args);
  public static void Error(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    s_logger.LogError(eventId, exception, message, args);

  public static void Critical(string? message, params object?[] args) =>
    s_logger.LogCritical(message, args);
  public static void Critical(EventId eventId, string? message, params object?[] args) =>
    s_logger.LogCritical(eventId, message, args);
  public static void Critical(Exception? exception, string? message, params object?[] args) =>
    s_logger.LogCritical(exception, message, args);
  public static void Critical(EventId eventId, Exception? exception, string? message, params object?[] args) =>
    s_logger.LogCritical(eventId, exception, message, args);
}
