/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Threading;

using MTGOSDK.Win32.API;


namespace ScubaDiver;

public class DllEntry
{
  private static void DiverHost(object pwzArgument)
  {
    try
    {
      // The port is the process ID by default, but can be overridden by
      // the bootstrap with the first argument given to the entry point.
      if (!ushort.TryParse((string)pwzArgument, out ushort port))
        port = (ushort)Process.GetCurrentProcess().Id;

      Diver _instance = new();
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
