/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.Extensions.Logging;


namespace MTGOSDK.Core.Logging;

/// <summary>
/// Options for configuring file logging.
/// </summary>
public readonly struct FileLoggerOptions
{
  /// <summary>
  /// The directory to store log files for the logger.
  /// </summary>
  public string LogDirectory { get; init; }

  /// <summary>
  /// The name of the log file.
  /// </summary>
  public string FileName { get; init; }

  /// <summary>
  /// The maximum age of log files before they are deleted.
  /// </summary>
  public TimeSpan? MaxAge { get; init; }

  /// <summary>
  /// A callback to format each log entry.
  /// </summary>
  public LineFormatter? Formatter { get; init; }

  /// <summary>
  /// The minimum log level to write to the log file.
  /// </summary>
  public LogLevel? LogLevel { get; init; }

  /// <summary>
  /// A delegate that represents a method to format log entries.
  /// </summary>
  public delegate string LineFormatter(
    DateTime timestamp,
    LogLevel level,
    string category,
    string message);
}
