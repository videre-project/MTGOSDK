/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.Diagnostics;
using System.IO;

using MTGOSDK.Win32.Extensions;

#if !NETSTANDARD2_0
using MTGOSDK.Resources;
#endif

namespace MTGOSDK.Core.Remoting;

public static class Bootstrapper
{
  public static string AppDataDir =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      ExtractDir
    );

  public static string ExtractDir = typeof(Bootstrapper).Assembly.GetName().Name;

  public static void Inject(Process target, ushort diverPort)
  {
#if !NETSTANDARD2_0
    // Not injected yet, Injecting adapter now (which should load the Diver)
    GetInjectionToolkit(target, out string launcherPath, out string diverPath);
    string adapterExecutionArg = string.Join("*",
        diverPath,
        "ScubaDiver.DllEntry",
        "EntryPoint",
        diverPort.ToString());

    var injectorProc = Process.Start(new ProcessStartInfo(launcherPath,
      $"{target.Id} {adapterExecutionArg}")
        {
          WorkingDirectory = AppDataDir,
          UseShellExecute = false,
          RedirectStandardOutput = true
        });
    if (injectorProc != null && injectorProc.WaitForExit(5000))
    {
      // Injector finished early, there's probably an error.
      if (injectorProc.ExitCode != 0)
      {
        var stderr = injectorProc.StandardError.ReadToEnd();
        throw new Exception("Injector returned error: " + stderr);
      }
    }
    else
    {
      // Stdout must be read to prevent deadlock when injector process exits.
      _ = injectorProc.StandardOutput.ReadToEnd();
    }
#endif
  }

#if !NETSTANDARD2_0
  private static void GetInjectionToolkit(
    Process target,
    out string launcherPath,
    out string diverPath)
  {
    DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(AppDataDir);
    if (!remoteNetAppDataDirInfo.Exists)
      remoteNetAppDataDirInfo.Create();

    byte[] launcherResource = target.Is64Bit()
      ? EmbeddedResources.GetBinaryResource(@"Resources\Launcher_x64.exe")
      : EmbeddedResources.GetBinaryResource(@"Resources\Launcher.exe");
    launcherPath = target.Is64Bit()
      ? Path.Combine(AppDataDir, "Launcher_x64.exe")
      : Path.Combine(AppDataDir, "Launcher.exe");

    byte[] adapterResource = target.Is64Bit()
      ? EmbeddedResources.GetBinaryResource(@"Resources\Bootstrapper_x64.dll")
      : EmbeddedResources.GetBinaryResource(@"Resources\Bootstrapper.dll");
    var adapterPath = target.Is64Bit()
      ? Path.Combine(AppDataDir, "Bootstrapper_x64.dll")
      : Path.Combine(AppDataDir, "Bootstrapper.dll");

    // Get the .NET diver assembly to inject into the target process
    byte[] diverResource = EmbeddedResources.GetBinaryResource(@"Resources\Microsoft.Diagnostics.Runtime.dll");
    diverPath = Path.Combine(AppDataDir, "Microsoft.Diagnostics.Runtime.dll");

    // Check if injector or bootstrap resources differ from copies on disk
    OverrideFileIfChanged(launcherPath, launcherResource);
    OverrideFileIfChanged(adapterPath, adapterResource);
    OverrideFileIfChanged(diverPath, diverResource);
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
#endif
}
