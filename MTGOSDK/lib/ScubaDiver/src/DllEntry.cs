/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

using MTGOSDK.Core.Logging;
using MTGOSDK.Resources;


namespace ScubaDiver;

public class DllEntry
{
  /// <summary>
  /// Resolve version-mismatched framework assemblies (e.g. System.ValueTuple)
  /// by finding an already-loaded assembly with a matching name. Registered
  /// in the static constructor so it runs before any other code in this class
  /// is JIT'd.
  /// </summary>
  static DllEntry()
  {
    AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLoadedAssemblies;
  }

  private static Assembly ResolveFromLoadedAssemblies(object sender, ResolveEventArgs e)
  {
    var requestedName = new AssemblyName(e.Name).Name;
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      if (asm.GetName().Name == requestedName) return asm;
    }
    return null;
  }

  /// <summary>
  /// Maps assembly simple name → full path for DLLs co-located with the
  /// Diver assembly. Built once at startup so the resolve handler can do a
  /// fast dictionary lookup instead of hitting the filesystem on every call.
  /// </summary>
  private static readonly Dictionary<string, string> s_localAssemblies = new(
    StringComparer.OrdinalIgnoreCase);

  private static void UseAssemblyLoadHook()
  {
    // Pre-scan the directory next to the Diver DLL and index every .dll
    // by its file name (without extension). This lets the resolve handler
    // skip file I/O and GetAssemblies() for assemblies we don't ship,
    // which avoids taking CLR-internal locks that serialize assembly
    // loading across all threads in the host process.
    string diverDir = Path.GetDirectoryName(typeof(DllEntry).Assembly.Location);
    if (diverDir != null)
    {
      foreach (string file in Directory.GetFiles(diverDir, "*.dll"))
      {
        string name = Path.GetFileNameWithoutExtension(file);
        s_localAssemblies[name] = file;
      }
    }

    AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
    {
      var requestedName = new AssemblyName(e.Name);

      // Load from disk only for assemblies we ship next to the Diver DLL.
      // The version-mismatch fallback (GetAssemblies scan) is handled by
      // the static constructor's ResolveFromLoadedAssemblies handler.
      if (s_localAssemblies.TryGetValue(requestedName.Name, out string path)
          && File.Exists(path))
      {
        try
        {
          return Assembly.LoadFrom(path);
        }
        catch (IOException) { }
        catch (BadImageFormatException) { }
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
      {
        Log.Debug("[DiverHost] Invalid port specified, using default.");
        port = (ushort) (Process.GetCurrentProcess().Id + 1024);
      }

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
    // Register the disk-loading hook for assemblies co-located with the Diver.
    // The version-mismatch fallback is already active from the static constructor.
    UseAssemblyLoadHook();

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
