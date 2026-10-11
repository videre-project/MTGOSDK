/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

namespace MTGOSDK.MSBuild;

/// <summary>
/// Identifies an enum import failure at a source location for build diagnostics.
/// </summary>
public sealed class EnumImportException : Exception
{
  public string Code { get; }
  public string File { get; }
  public int Line { get; }

  public EnumImportException(string code, string file, int line, string message)
    : base(message)
  {
    Code = code;
    File = file;
    Line = line;
  }
}
