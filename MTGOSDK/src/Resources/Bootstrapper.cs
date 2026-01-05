/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Win32.Extensions;
using MTGOSDK.Win32.Injection;


namespace MTGOSDK.Resources;
using static MTGOSDK.Resources.EmbeddedResources;

public enum DiverState
{
  NoDiver,
  Alive,
  Corpse,
  HollowSnapshot
}

public static class Bootstrapper
{
  public static string AppDataDir =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      ExtractDir
    );

  public static string ExtractDir = typeof(Bootstrapper).Assembly.GetName().Name;

  public static DiverState QueryStatus(
    Process target,
    string diverAddr,
    ushort diverPort)
  {
    DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);

    // We WANT to check liveness of the diver using HTTP but this might take a
    // LOT of time if it is dead (Trying to TCP SYN several times, with a
    // timeout between each). So a simple circuit-breaker is implemented
    // before that: If we manage to bind to the expected diver endpoint, we
    // assume it's not alive

    bool diverPortIsFree = false;
    try
    {
      IPAddress localAddr = IPAddress.Parse(diverAddr);
      TcpListener server = new TcpListener(localAddr, diverPort);
      server.Start();
      diverPortIsFree = true;
      server.Stop();
    }
    catch
    {
      // Had some issues, perhaps it's the diver holding that port.
    }

    if (!diverPortIsFree && com.CheckAliveness())
    {
      return DiverState.Alive;
    }

    // // Check if this is a snapshot created by the diver.
    // if (target.Threads.Count == 0)
    //   return DiverState.HollowSnapshot;

    // Diver isn't alive. It's possible that it was never injected or it was
    // injected and killed
    bool containsToolkitDll = false;
    try
    {
      containsToolkitDll |= target.Modules.AsEnumerable()
        .Any(module => module.ModuleName.Contains("Bootstrapper"));
    }
    catch
    {
      // Sometimes this happens because of x32 vs x64 process interaction
    }
    if (containsToolkitDll)
    {
      return DiverState.Corpse;
    }

    return DiverState.NoDiver;
  }

  /// <summary>
  /// Verifies that a file exists on disk, with retry logic for non-deterministic
  /// file availability issues that can occur after extraction.
  /// </summary>
  private static void VerifyFileExists(string filePath, string fileName)
  {
    const int maxRetries = 10;
    const int delayMs = 50;

    for (int i = 0; i < maxRetries; i++)
    {
      if (File.Exists(filePath))
      {
        // Verify file is not empty (indicates incomplete write)
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > 0)
          return;
      }
      Thread.Sleep(delayMs);
    }

    throw new FileNotFoundException(
      $"Failed to verify {fileName} exists after extraction. " +
      $"Path: {filePath}");
  }

  public static void Inject(Process target, ushort diverPort)
  {
#if !MTGOSDKCORE
    // Create the extraction directory if it doesn't exist
    DirectoryInfo AppDataDirInfo = new DirectoryInfo(AppDataDir);
    if (!AppDataDirInfo.Exists) AppDataDirInfo.Create();

    // Create a random temporary directory to be deleted when the process exits
    string tempDir = Path.Combine(AppDataDir, Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    AppDomain.CurrentDomain.ProcessExit += delegate
    {
      try
      {
        Directory.Delete(tempDir, true);
      }
      catch (UnauthorizedAccessException)
      {
        // Given we had permissions to write this directory, we assume this
        // is caused by a file lock from the MTGO process (if it's still running).
      }
    };

    // Get the .NET diver assembly to inject into the target process
    byte[] diverResource = GetBinaryResource(@"Resources\Microsoft.Diagnostics.Runtime.dll");
    string diverPath = Path.Combine(tempDir, "Microsoft.Diagnostics.Runtime.dll");

    // Check if injector or bootstrap resources differ from copies on disk
    OverrideFileIfChanged(diverPath, diverResource);

    // Update all diver dependencies
    byte[] harmonyResource = GetBinaryResource(@"Resources\0Harmony.dll");
    string harmonyPath = Path.Combine(tempDir, "0Harmony.dll");
    OverrideFileIfChanged(harmonyPath, harmonyResource);

    // Verify all files are fully written before injection
    VerifyFileExists(diverPath, "Microsoft.Diagnostics.Runtime.dll");
    VerifyFileExists(harmonyPath, "0Harmony.dll");

    var injector = new InjectorBase();
    injector.Inject(target, diverPath, "ScubaDiver.DllEntry", "EntryPoint");
#endif
  }
}
