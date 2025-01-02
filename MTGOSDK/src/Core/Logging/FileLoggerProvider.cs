/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

using MTGOSDK.Core.Reflection;


namespace MTGOSDK.Core.Logging;

public class FileLoggerProvider(FileLoggerOptions options)
    : DLRWrapper, ILoggerProvider
{
  private readonly ConcurrentDictionary<string, StreamWriter> _fileHandles = new();

  /// <summary>
  /// Gets the log file stream based on the provided options.
  /// </summary>
  /// <param name="options">The options to use for logging.</param>
  /// <returns>The log file stream.</returns>
  private StreamWriter GetLogFile(FileLoggerOptions options)
  {
    string filePath = Path.Combine(options.LogDirectory, options.FileName);
    if (!Directory.Exists(options.LogDirectory))
    {
      _ = Directory.CreateDirectory(options.LogDirectory);
    }
    // Delete old log files if the max age is set
    else if (options.MaxAge.HasValue)
    {
      foreach (var oldFile in Directory.GetFiles(options.LogDirectory))
      {
        if (File.GetCreationTime(oldFile) < DateTime.Now - options.MaxAge)
        {
          File.Delete(oldFile);
        }
      }
    }

    if (!_fileHandles.TryGetValue(filePath, out var file))
    {
      // Create a new log file if it does not exist
      if (!File.Exists(filePath))
      {
        File.Create(filePath).Dispose();
      }

      file = Retry(() => new StreamWriter(filePath));
      _ = _fileHandles.TryAdd(filePath, file);
    }
    return file;
  }

  public ILogger CreateLogger(string categoryName)
  {
    return new FileLogger(categoryName, GetLogFile(options), options);
  }

  public void Dispose()
  {
    foreach (var logFile in _fileHandles.Values)
    {
      lock (logFile)
      {
        logFile.Dispose();
      }
    }
    _fileHandles.Clear();
  }
}
