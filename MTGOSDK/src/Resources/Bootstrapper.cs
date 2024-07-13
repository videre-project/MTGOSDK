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

  public static void Inject(Process target, ushort diverPort)
  {
#if !MTGOSDKCORE
    // Create the extraction directory if it doesn't exist
    DirectoryInfo AppDataDirInfo = new DirectoryInfo(AppDataDir);
    if (!AppDataDirInfo.Exists) AppDataDirInfo.Create();

    // Get the .NET diver assembly to inject into the target process
    byte[] diverResource = EmbeddedResources.GetBinaryResource(@"Resources\Microsoft.Diagnostics.Runtime.dll");
    string diverPath = Path.Combine(AppDataDir, "Microsoft.Diagnostics.Runtime.dll");

    // Check if injector or bootstrap resources differ from copies on disk
    OverrideFileIfChanged(diverPath, diverResource);

    var injector = new InjectorBase();
    injector.Inject(target, diverPath, "ScubaDiver.DllEntry", "EntryPoint");
#endif
  }

  private static void OverrideFileIfChanged(string filePath, byte[] data)
  {
    bool fileChanged = true;

    if (File.Exists(filePath))
    {
      using (FileStream file = new(filePath, FileMode.Open, FileAccess.Read))
      {
        if (file.Length == data.Length)
        {
          fileChanged = false;
          for (int i = 0; i < file.Length; i++)
          {
            if (file.ReadByte() != data[i])
            {
              fileChanged = true;
              break;
            }
          }
        }
      }
    }

    if (fileChanged)
    {
      File.WriteAllBytes(filePath, data);
    }
  }
}
