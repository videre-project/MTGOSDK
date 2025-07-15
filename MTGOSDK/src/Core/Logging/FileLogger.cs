/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using Microsoft.Extensions.Logging;

using LineFormatter = MTGOSDK.Core.Logging.FileLoggerOptions.LineFormatter;


namespace MTGOSDK.Core.Logging;

public class FileLogger(
  string category,
  StreamWriter logFile,
  FileLoggerOptions options) : ILogger
{
  public static readonly LineFormatter DefaultLineFormatter =
    new((timestamp, level, category, message) =>
      $"{timestamp:O} [{level}] [{category}] {message}");

#pragma warning disable CS8633
  public IDisposable BeginScope<TState>(TState state) => null;
#pragma warning restore CS8633

  public bool IsEnabled(LogLevel logLevel) =>
    logLevel >= (options.LogLevel ?? LogLevel.Debug);

  public void Log(string message)
  {
    lock (logFile)
    {
      logFile.WriteLine(message);
      logFile.Flush();
    }
  }

  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception exception,
    Func<TState, Exception, string> formatter)
  {
    if (!IsEnabled(logLevel) || logFile == null) return;

    // Get the formatted log message
    var timestamp = DateTime.Now;
    string message = formatter(state, exception);
    var lineFormatter = options.Formatter ?? DefaultLineFormatter;

    // Write log messages to text file
    Log(lineFormatter(timestamp, logLevel, category, message));
  }
}
