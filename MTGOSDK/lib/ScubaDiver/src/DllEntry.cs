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
  private static readonly object s_resolveLock = new();
  private static bool s_resolveInitialized;

  /// <summary>
  /// Resolve version-mismatched framework assemblies (e.g. System.ValueTuple)
  /// by finding an already-loaded assembly with a matching name. Registered
  /// in the static constructor so it runs before any other code in this class
  /// is JIT'd.
  /// </summary>
  static DllEntry()
  {
    InitializeAssemblyResolve();
  }

  private static void InitializeAssemblyResolve()
  {
    lock (s_resolveLock)
    {
      if (s_resolveInitialized)
        return;

      IndexAssemblyDirectory(AppDomain.CurrentDomain.BaseDirectory);
      IndexAssemblyDirectory(Path.GetDirectoryName(typeof(DllEntry).Assembly.Location));
      AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
      s_resolveInitialized = true;
    }
  }

  private static Assembly ResolveAssembly(object sender, ResolveEventArgs e)
  {
    var requestedAssembly = new AssemblyName(e.Name);
    var requestedName = requestedAssembly.Name;

    // Prefer assemblies shipped with the Diver payload over whatever the host
    // process has loaded — this avoids host probing-policy differences and
    // version conflicts with MTGO's own dependencies (e.g. System.ValueTuple).
    if (s_localAssemblies.TryGetValue(requestedName, out string path) && File.Exists(path))
    {
      try
      {
        var fileAssembly = AssemblyName.GetAssemblyName(path);
        if (AsmMatchesRequestedVersion(fileAssembly, requestedAssembly))
          return Assembly.LoadFrom(path);
      }
      catch (IOException) { }
      catch (BadImageFormatException) { }
    }

    // Fall back to already-loaded assemblies (handles version-mismatch redirects
    // for framework assemblies not shipped with the Diver).
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      if (AsmMatchesRequestedVersion(asm, requestedAssembly))
        return asm;
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

  private static bool AsmMatchesRequestedVersion(Assembly assembly, AssemblyName requested)
  {
    return AsmMatchesRequestedVersion(assembly.GetName(), requested);
  }

  /// <summary>
  /// Returns true when <paramref name="candidate"/> can satisfy a reference to
  /// <paramref name="requested"/>: same name, matching public-key token (when
  /// both are strong-named), and a version that is equal-or-newer within the
  /// same major.minor line (e.g. 4.0.2.0 satisfies a request for 4.0.1.0).
  /// </summary>
  private static bool AsmMatchesRequestedVersion(AssemblyName candidate, AssemblyName requested)
  {
    if (!candidate.Name.Equals(requested.Name, StringComparison.OrdinalIgnoreCase))
      return false;

    byte[] requestedPkt = requested.GetPublicKeyToken();
    byte[] candidatePkt = candidate.GetPublicKeyToken();
    if (requestedPkt is { Length: > 0 } && candidatePkt is { Length: > 0 })
    {
      if (!PublicKeyTokenEquals(requestedPkt, candidatePkt))
        return false;
    }

    if (requested.Version == null || candidate.Version == null)
      return true;

    if (candidate.Version.Major != requested.Version.Major ||
        candidate.Version.Minor != requested.Version.Minor)
      return false;

    return candidate.Version >= requested.Version;
  }

  private static bool PublicKeyTokenEquals(byte[] a, byte[] b)
  {
    if (a.Length != b.Length)
      return false;

    for (int i = 0; i < a.Length; i++)
    {
      if (a[i] != b[i])
        return false;
    }

    return true;
  }

  private static void IndexAssemblyDirectory(string directory)
  {
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
      return;

    // Pre-scan the directory and index every .dll by file name (without
    // extension). This lets the resolve handler skip file I/O and
    // GetAssemblies() for assemblies we don't ship.
    foreach (string file in Directory.GetFiles(directory, "*.dll"))
    {
      string name = Path.GetFileNameWithoutExtension(file);
      s_localAssemblies[name] = file;
    }
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
    // Ensure resolve hooks are initialized before Diver startup.
    InitializeAssemblyResolve();

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
