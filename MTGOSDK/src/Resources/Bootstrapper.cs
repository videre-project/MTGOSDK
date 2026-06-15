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


namespace MTGOSDK.Resources;

public enum DiverState
{
  NoDiver,
  Alive,
  Corpse,
  HollowSnapshot
}
public interface IBootstrapperRuntime
{
  byte[] GetBinaryResource(string name);

  void OverrideFileIfChanged(string filePath, byte[] data);

  void Inject(Process target, ushort diverPort);
}

public static class Bootstrapper
{
  private static IBootstrapperRuntime? s_runtime;

  public static string AppDataDir =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      ExtractDir
    );

  public static string ExtractDir = "MTGOSDK";

  public static void Configure(IBootstrapperRuntime runtime) =>
    s_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

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

  public static byte[] GetBinaryResource(string name) =>
    GetRuntime().GetBinaryResource(name);

  public static void OverrideFileIfChanged(string filePath, byte[] data) =>
    GetRuntime().OverrideFileIfChanged(filePath, data);

  public static void Inject(Process target, ushort diverPort) =>
    GetRuntime().Inject(target, diverPort);

  private static IBootstrapperRuntime GetRuntime() =>
    s_runtime ?? throw new InvalidOperationException(
      "MTGOSDK bootstrapper runtime has not been configured. " +
      "Reference the MTGOSDK assembly before using resource extraction or injection APIs.");
}
