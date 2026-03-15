/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.Extensions.Logging;


namespace MTGOSDK.Core.Logging;

/// <summary>
/// A class that suppresses logging within the current async context.
/// </summary>
/// <remarks>
/// This class creates a context that suppresses all logging until the context
/// is disposed. Uses AsyncLocal to properly flow suppression across async
/// boundaries, unlike stack-trace scanning which breaks across async calls.
/// </remarks>
public class SuppressionContext : IDisposable
{
  /// <summary>
  /// AsyncLocal to track suppression state across async boundaries.
  /// When set, all logging at or below the specified level is suppressed.
  /// </summary>
  private static readonly AsyncLocal<LogLevel?> s_suppressionLevel = new();

  /// <summary>
  /// Thread-local suppression state that does not flow across async/task boundaries.
  /// Useful for muting transient retry noise in setup code that may spawn long-lived tasks.
  /// </summary>
  [ThreadStatic]
  private static LogLevel? t_suppressionLevel;

  /// <summary>
  /// Track previous suppression level to restore on dispose (for nesting).
  /// </summary>
  private readonly LogLevel? _previousLevel;
  private readonly LogLevel? _previousThreadLevel;
  private readonly bool _flowAcrossAsync;

  public SuppressionContext(LogLevel logLevel = LogLevel.None, bool flowAcrossAsync = true)
  {
    _flowAcrossAsync = flowAcrossAsync;
    _previousLevel = s_suppressionLevel.Value;
    _previousThreadLevel = t_suppressionLevel;

    if (_flowAcrossAsync)
    {
      // Store previous level for proper nesting support
      s_suppressionLevel.Value = logLevel;
    }
    else
    {
      t_suppressionLevel = logLevel;
    }
  }

  /// <summary>
  /// Checks if logging is currently suppressed in this async context.
  /// </summary>
  /// <returns>True if logging is suppressed.</returns>
  public static bool IsSuppressed() =>
    s_suppressionLevel.Value != null || t_suppressionLevel != null;

  /// <summary>
  /// Checks if a specific log level is suppressed in this async context.
  /// </summary>
  /// <param name="level">The log level to check.</param>
  /// <returns>True if the level is suppressed.</returns>
  public static bool IsSuppressed(LogLevel level)
  {
    var suppressionLevel = s_suppressionLevel.Value ?? t_suppressionLevel;
    // If suppression is enabled and the log level is at or below the threshold
    return suppressionLevel.HasValue && level <= suppressionLevel.Value;
  }

  public void Dispose()
  {
    // Restore previous suppression level (supports nesting)
    if (_flowAcrossAsync)
    {
      s_suppressionLevel.Value = _previousLevel;
    }
    else
    {
      t_suppressionLevel = _previousThreadLevel;
    }
  }
}
