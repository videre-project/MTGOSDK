/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MTGOSDK.Core.Compiler;


namespace MTGOSDK.Core.Logging;

/// <summary>
/// A provider for creating loggers with options.
/// </summary>
[ProviderAlias($"{nameof(LogOptionsProvider<TLogger, TConfiguration>)}")]
public sealed class LogOptionsProvider<TLogger, TConfiguration>
  : ILoggerProvider
    where TLogger : ILogger, new()
    where TConfiguration : class, new()
{
  private readonly IDisposable? _onChangeToken;
  private TConfiguration _currentConfig;
  private readonly ConcurrentDictionary<string, TLogger> _loggers =
    new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Initializes a new instance of the <see cref="LogOptionsProvider{TLogger, TConfiguration}"/> class
  /// with the specified configuration.
  /// </summary>
  public LogOptionsProvider(IOptionsMonitor<TConfiguration> config)
  {
    _currentConfig = config.CurrentValue;
    _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
  }

  /// <summary>
  /// Creates a new logger instance with the specified category.
  /// </summary>
  public ILogger CreateLogger(string category) =>
    _loggers.GetOrAdd(category, name => CreateLoggerInstance(category));

  private TConfiguration GetCurrentConfig() => _currentConfig;

  private TLogger CreateLoggerInstance(string category) =>
    (TLogger)
    InstanceFactory.CreateInstance(typeof(TLogger), category, GetCurrentConfig);

  /// <summary>
  /// Disposes of the provider and all loggers.
  /// </summary>
  public void Dispose()
  {
    _loggers.Clear();
    _onChangeToken?.Dispose();
  }
}
