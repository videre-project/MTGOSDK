/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Threading;
using System.Diagnostics;
using System.Linq;


namespace ScubaDiver;

public class DllEntry
{
  private static bool UnloadBootstrapper()
  {
    foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
    {
      if (new string[] {
          "Bootstrapper.dll",
          "Bootstrapper_x64.dll"
        }.Any(s => module.ModuleName == s))
      {
        return Kernel32.FreeLibrary(module.BaseAddress);
      }
    }
    return false;
  }

  private static void DiverHost(object pwzArgument)
  {
    try
    {
      Diver _instance = new();
      ushort port = ushort.Parse((string)pwzArgument);
      _instance.Start(port);

      // Diver killed (politely)
      Logger.Debug("[DiverHost] Diver finished gracefully.");
    }
    catch (Exception e)
    {
      Logger.Debug("[DiverHost] ScubaDiver crashed.");
      Logger.Debug(e.ToString());
      Logger.Debug("[DiverHost] Exiting entry point in 10 seconds.");
      Thread.Sleep(TimeSpan.FromSeconds(10));
    }
  }

  public static int EntryPoint(string pwzArgument)
  {
    // If we need to log and a debugger isn't attached to the target process
    // then we need to allocate a console and redirect STDOUT to it.
    Logger.RedirectConsole();

    // Unload the native bootstrapper DLL to free up the file handle.
    if (!UnloadBootstrapper())
    {
      Logger.Debug("[EntryPoint] Failed to unload Bootstrapper.");
      return 1;
    }

    // The Bootstrapper needs to call a C# function with exactly this signature,
    // so we use it to just create a diver, and run the Start func (blocking)
    ParameterizedThreadStart func = DiverHost;
    Logger.Debug($"[EntryPoint] Starting ScubaDiver with argument: {pwzArgument}");
    Thread diverHostThread = new(func);
    diverHostThread.Start(pwzArgument);

    Logger.Debug("[EntryPoint] Returning");
    return 0;
  }
}
