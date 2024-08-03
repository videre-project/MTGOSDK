/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Microsoft.Extensions.Logging;


namespace MTGOSDK.NUnit.Logging;

public class NUnitLoggerProvider(LogLevel minLevel, DateTimeOffset? logStart)
    : ILoggerProvider
{
  public NUnitLoggerProvider() : this(LogLevel.Trace) { }

  public NUnitLoggerProvider(LogLevel minLevel) : this(minLevel, null) { }

  public ILogger CreateLogger(string categoryName) =>
    new NUnitLogger(categoryName, minLevel, logStart);

  public void Dispose() { }
}
