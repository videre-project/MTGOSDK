/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.Logging;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.UnitTests;

/// <summary>
/// The static <see cref="Log"/> methods honour <see cref="LoggerBase.MinimumLevel"/>
/// at every level, without creating a logger for a disabled message.
/// </summary>
[NonParallelizable]
public class LogThreshold
{
  /// <summary>
  /// Records every entry its loggers receive and every logger it creates.
  /// </summary>
  private sealed class RecordingProvider : ILoggerProvider
  {
    public List<LogLevel> Entries { get; } = new();
    public int LoggersCreated { get; private set; }

    public ILogger CreateLogger(string categoryName)
    {
      LoggersCreated++;
      return new Recorder(this);
    }

    public void Dispose() { }

    private sealed class Recorder(RecordingProvider provider) : ILogger
    {
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) =>
        provider.Entries.Add(logLevel);
    }
  }

  private static readonly LogLevel[] s_levels =
  [
    LogLevel.Trace, LogLevel.Debug, LogLevel.Information,
    LogLevel.Warning, LogLevel.Error, LogLevel.Critical,
  ];

  /// <summary>
  /// Logs once through each of the four overload shapes at <paramref name="level"/>.
  /// </summary>
  private static void LogEveryOverload(LogLevel level)
  {
    var id = new EventId(1);
    var ex = new InvalidOperationException();
    switch (level)
    {
      case LogLevel.Trace:
        Log.Trace("m"); Log.Trace(id, "m"); Log.Trace(ex, "m"); Log.Trace(id, ex, "m"); break;
      case LogLevel.Debug:
        Log.Debug("m"); Log.Debug(id, "m"); Log.Debug(ex, "m"); Log.Debug(id, ex, "m"); break;
      case LogLevel.Information:
        Log.Information("m"); Log.Information(id, "m"); Log.Information(ex, "m"); Log.Information(id, ex, "m"); break;
      case LogLevel.Warning:
        Log.Warning("m"); Log.Warning(id, "m"); Log.Warning(ex, "m"); Log.Warning(id, ex, "m"); break;
      case LogLevel.Error:
        Log.Error("m"); Log.Error(id, "m"); Log.Error(ex, "m"); Log.Error(id, ex, "m"); break;
      case LogLevel.Critical:
        Log.Critical("m"); Log.Critical(id, "m"); Log.Critical(ex, "m"); Log.Critical(id, ex, "m"); break;
    }
  }

  /// <summary>
  /// The provider the process uses now (LoggerBase has no public getter).
  /// </summary>
  private static ILoggerProvider CurrentProvider =>
    (ILoggerProvider)typeof(LoggerBase)
      .GetField("s_provider", BindingFlags.Static | BindingFlags.NonPublic)!
      .GetValue(null)!;

  private static RecordingProvider Record(LogLevel minimum, Action log)
  {
    var provider = new RecordingProvider();
    LogLevel previousLevel = LoggerBase.MinimumLevel;
    ILoggerProvider previousProvider = CurrentProvider;
    // A new provider clears the cached loggers.
    LoggerBase.SetProviderInstance(provider);
    LoggerBase.MinimumLevel = minimum;
    try
    {
      log();
    }
    finally
    {
      LoggerBase.MinimumLevel = previousLevel;
      LoggerBase.SetProviderInstance(previousProvider);
    }
    return provider;
  }

  [TestCase(LogLevel.Trace)]
  [TestCase(LogLevel.Information)]
  [TestCase(LogLevel.Warning)]
  [TestCase(LogLevel.Critical)]
  public void Test_EveryLevel_HonoursTheThreshold(LogLevel minimum)
  {
    var provider = Record(minimum, () =>
    {
      foreach (var level in s_levels) LogEveryOverload(level);
    });

    var expected = s_levels
      .Where(level => level >= minimum)
      .SelectMany(level => Enumerable.Repeat(level, 4));
    Assert.That(provider.Entries, Is.EqualTo(expected));
  }

  [Test]
  public void Test_NoneDisablesEveryLevel_WithoutCreatingALogger()
  {
    var provider = Record(LogLevel.None, () =>
    {
      foreach (var level in s_levels) LogEveryOverload(level);
    });

    Assert.That(provider.Entries, Is.Empty);
    Assert.That(provider.LoggersCreated, Is.EqualTo(0), "disabled logging does not request a logger from the provider");
  }

  [Test]
  public void Test_ADisabledMessage_CreatesNoLogger()
  {
    var provider = Record(LogLevel.Warning, () => LogEveryOverload(LogLevel.Debug));
    Assert.That(provider.LoggersCreated, Is.EqualTo(0));

    provider = Record(LogLevel.Warning, () => LogEveryOverload(LogLevel.Error));
    Assert.That(provider.LoggersCreated, Is.GreaterThan(0));
    Assert.That(provider.Entries, Has.Count.EqualTo(4));
  }
}
