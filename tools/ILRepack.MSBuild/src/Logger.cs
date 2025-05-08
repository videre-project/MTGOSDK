/** @file
  Copyright (c) 2004, Evain Jb (jb@evain.net)
  Modified 2007 Marcus Griep (neoeinstein+boo@gmail.com)
  Modified 2013 Peter Sunde (peter.sunde@gmail.com)
  Modified 2016-2024 Ravi Patel (rbsoft.org)
  All rights reserved.
  SPDX-License-Identifier: BSD-3-Clause
**/

using System;
using System.IO;

using Microsoft.Extensions.Logging;

using ILogger = ILRepacking.ILogger;


namespace ILRepack.Lib.MSBuild.Task;

internal class Logger : ILogger
{
  private string _outputFile;
  private StreamWriter _writer;

  private LogLevel _logLevel { get; set; } = LogLevel.Warning;
  public LogLevel LogLevel
  {
    get => _logLevel;
    set
    {
      _logLevel = value;
      ShouldLogVerbose = value >= LogLevel.Trace;
    }
  }

  public bool ShouldLogVerbose { get; set; } = false;

  public void Error(string msg)
  {
    Log($"ERROR: {msg}", LogLevel.Error);
  }

  public void Warn(string msg)
  {
    Log($"WARN: {msg}", LogLevel.Warning);
  }

  public void Info(string msg)
  {
    Log($"INFO: {msg}", LogLevel.Information);
  }

  public void Verbose(string msg)
  {
    if (ShouldLogVerbose)
    {
      Log($"VERBOSE: {msg}", LogLevel.Trace);
    }
  }

  public void Log(object str, LogLevel level)
  {
    // Always write to the output file regardless of the log level
    var logStr = str.ToString();
    _writer?.WriteLine(logStr);

    // Filter messages sent to the console based on the log level
    if (level >= _logLevel)
    {
      Console.WriteLine(logStr);
    }
  }

  public bool Open(string file)
  {
    if (string.IsNullOrEmpty(file))
    {
      return false;
    }

    _outputFile = file;
    var directory = Path.GetDirectoryName(_outputFile);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    _writer = new StreamWriter(_outputFile);
    return true;
  }

  public void Close()
  {
    if (_writer == null)
    {
      return;
    }

    _writer.Close();
    _writer = null;
  }

  public void DuplicateIgnored(string ignoredType, object ignoredObject)
  {
    // TODO: put on a list and log a summary
    //INFO("Ignoring duplicate " + ignoredType + " " + ignoredObject);
  }
}
