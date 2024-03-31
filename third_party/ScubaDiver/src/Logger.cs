/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Diagnostics;


namespace ScubaDiver;

internal class Logger
{
#if DEBUG
  public static bool IsDebug = true;
#else
  public static bool IsDebug = false;
#endif

  internal static void Debug(string s)
  {
    if (IsDebug || Debugger.IsAttached)
    {
      System.Diagnostics.Debug.WriteLine(s);
    }
  }
}
