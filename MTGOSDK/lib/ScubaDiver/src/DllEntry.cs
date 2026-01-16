/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

using MTGOSDK.Core.Logging;
using MTGOSDK.Resources;


namespace ScubaDiver;

public class DllEntry
{
  private static Assembly LoadAssembly(string name)
  {
    string assemblyPath = Path.Combine(
      Path.GetDirectoryName(typeof(DllEntry).Assembly.Location),
      name + ".dll"
    );

    // Retry logic for non-deterministic file availability issues
    // The file may not be immediately available after extraction
    const int maxRetries = 5;
    const int initialDelayMs = 50;

    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
      if (File.Exists(assemblyPath))
      {
        try
        {
          return Assembly.LoadFrom(assemblyPath);
        }
        catch (IOException) when (attempt < maxRetries - 1)
        {
          // File exists but may be locked; wait and retry
          Thread.Sleep(initialDelayMs * (1 << attempt)); // Exponential backoff
        }
        catch (BadImageFormatException)
        {
          // File is corrupted or incomplete; wait for it to be fully written
          Thread.Sleep(initialDelayMs * (1 << attempt));
        }
      }
      else if (attempt < maxRetries - 1)
      {
        // File doesn't exist yet; wait for extraction to complete
        Thread.Sleep(initialDelayMs * (1 << attempt));
      }
    }

    return null;
  }

  private static void UseAssemblyLoadHook()
  {
    //
    // Add a hook to resolve assemblies that can't be found.
    //
    // First tries to load from disk next to ScubaDiver's location.
    // Then falls back to finding any already-loaded assembly with matching name
    // (ignoring version). This fixes version mismatches with assemblies like
    // System.Numerics.Vectors, System.Memory, etc.
    //
    AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
    {
      var requestedName = new AssemblyName(e.Name);

      // First, try loading from disk next to the current assembly
      var diskAssembly = LoadAssembly(requestedName.Name);
      if (diskAssembly != null) return diskAssembly;

      // Fallback: Use already-loaded assembly with matching name (any version)
      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        if (asm.GetName().Name == requestedName.Name) return asm;
      }

      return null;
    };
  }

  private static void DiverHost(object pwzArgument)
  {
    try
    {
      // The port is the process ID by default, but can be overridden by
      // the bootstrap with the first argument given to the entry point.
      if (!ushort.TryParse((string) pwzArgument, out ushort port))
        port = (ushort) Process.GetCurrentProcess().Id;

      // Configure logging options to write to a dedicated log file sink.
      Bootstrapper.ExtractDir = "MTGOSDK";
      FileLoggerOptions options = new()
      {
        LogDirectory = Path.Combine(Bootstrapper.AppDataDir, "Logs"),
        FileName = $"Diver-{port}.log",
        MaxAge = TimeSpan.FromDays(3),
      };
      LoggerBase.SetProviderInstance(new FileLoggerProvider(options));

      // Register a handler for uncaught exceptions to log them.
      AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
      {
        Log.Error("[DiverHost] Unhandled exception occurred.");
        Log.Error(e.ExceptionObject.ToString());
      };

      // Load any dependencies that are not in the GAC.
      UseAssemblyLoadHook();

      // Start the diver instance and block the thread until it exits.
      Diver _instance = new();
      _instance.Start(port);
      Log.Debug("[DiverHost] Diver finished gracefully.");
    }
    catch (Exception e)
    {
      Log.Debug("[DiverHost] ScubaDiver crashed.");
      Log.Debug(e.ToString());
      Log.Debug("[DiverHost] Exiting entry point in 10 seconds.");
      Thread.Sleep(TimeSpan.FromSeconds(10));
    }
  }

  public static int EntryPoint(string pwzArgument)
  {
    // The bootstrapper is expecting to call a C# function with this signature,
    // so we use it to start a new thread to host the diver in it's own thread.
    ParameterizedThreadStart func = DiverHost;
    Thread diverHostThread = new(func)
    {
      Name = "DiverHostThread",
      IsBackground = true,
    };
    diverHostThread.SetApartmentState(ApartmentState.STA);
    diverHostThread.Start(pwzArgument);

    // Block the thread until the diver has exited.
    // This may cause a deadlock if the diver crashes in a non-recoverable way,
    // so we handle that case in the <see cref="DiverHost"/> function.
    diverHostThread.Join();

    // Signal to the launcher that the diver has finished bootstrapping.
    return 0;
  }
}
