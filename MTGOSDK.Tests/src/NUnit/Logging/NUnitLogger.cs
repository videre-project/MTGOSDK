/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging;


namespace MTGOSDK.NUnit.Logging;

public class NUnitLogger(
  string category,
  LogLevel minLogLevel,
  DateTimeOffset? logStart) : ILogger
{
  private static readonly string[] NewLineChars = new[] { Environment.NewLine };

  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter)
  {
    if (!IsEnabled(logLevel)) return;

    // Buffer the message into a single string in order to avoid shearing the
    // message when running across multiple threads.
    var messageBuilder = new StringBuilder();

    var timestamp = logStart.HasValue ?
      $"{(DateTimeOffset.UtcNow - logStart.Value).TotalSeconds.ToString("N3", CultureInfo.InvariantCulture)}s" :
      DateTimeOffset.UtcNow.ToString("s", CultureInfo.InvariantCulture);

    var firstLinePrefix = $"| [{timestamp}] {category} {logLevel}: ";
    var lines = formatter(state, exception).Split(NewLineChars, StringSplitOptions.RemoveEmptyEntries);
    messageBuilder.AppendLine(firstLinePrefix + lines.FirstOrDefault() ?? string.Empty);

    var additionalLinePrefix = "|" + new string(' ', firstLinePrefix.Length - 1);
    foreach (var line in lines.Skip(1))
    {
      messageBuilder.AppendLine(additionalLinePrefix + line);
    }

    if (exception != null)
    {
      lines = exception.ToString().Split(NewLineChars, StringSplitOptions.RemoveEmptyEntries);
      additionalLinePrefix = "| ";
      foreach (var line in lines)
      {
        messageBuilder.AppendLine(additionalLinePrefix + line);
      }
    }

    // Remove the last line-break, because ITestOutputHelper only has WriteLine.
    var message = messageBuilder.ToString();
    if (message.EndsWith(Environment.NewLine, StringComparison.Ordinal))
    {
      message = message.Substring(0, message.Length - Environment.NewLine.Length);
    }

    try
    {
      // TestContext.Progress.WriteLine(message); // Requires '--logger "Console;Verbosity=normal"'
      TestContext.WriteLine(message);
    }
    catch (Exception)
    {
      // We could fail because we're on a background thread and our captured
      // ITestOutputHelper is busted (if the test "completed" before the
      // background thread fired).
      // So, ignore this. There isn't really anything we can do but hope the
      // caller has additional loggers registered
    }
  }

  public bool IsEnabled(LogLevel logLevel) => logLevel >= minLogLevel;

#pragma warning disable CS8633 // Mismatch in nullability TState constraints.
  public IDisposable BeginScope<TState>(TState state) => new NullScope();
#pragma warning restore CS8633 // Mismatch in nullability TState constraints.

  private class NullScope : IDisposable
  {
    public void Dispose() { }
  }
}
