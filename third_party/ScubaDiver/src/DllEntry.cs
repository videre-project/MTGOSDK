/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Threading;
using System.Diagnostics;
using System.Linq;

using MTGOSDK.Win32.API;


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

    // The bootstrapper is expecting to call a C# function with this signature,
    // so we use it to start a new thread to host the diver in it's own thread.
    ParameterizedThreadStart func = DiverHost;
    Thread diverHostThread = new(func);
    diverHostThread.Start(pwzArgument);

    // Block the thread until the diver has exited.
    // This may cause a deadlock if the diver crashes in a non-recoverable way,
    // so we handle that case in the <see cref="DiverHost"/> function.
    diverHostThread.Join();

    return 0;
  }
}
