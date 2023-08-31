using System;
using System.Diagnostics;
using System.IO;

using RemoteNET.Internal.Extensions;
using RemoteNET.Properties;


namespace RemoteNET
{
  public static class Bootstrapper
  {
    public static string AppDataDir =>
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ExtractDir);
    public static string ExtractDir = typeof(RemoteApp).Assembly.GetName().Name;

    public static void Inject(Process target, ushort diverPort)
    {
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
    }

    private static void GetInjectionToolkit(
      Process target,
      out string launcherPath,
      out string diverPath)
    {
      // Dumping injector + adapter DLL to a %localappdata%\RemoteNET
      DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(AppDataDir);
      if (!remoteNetAppDataDirInfo.Exists)
        remoteNetAppDataDirInfo.Create();

      // Decide which injection toolkit to use x32 or x64
      byte[] launcherResource = target.Is64Bit()
        ? Resources.Launcher_x64
        : Resources.Launcher;
      launcherPath = target.Is64Bit()
        ? Path.Combine(AppDataDir, "Launcher_x64.exe")
        : Path.Combine(AppDataDir, "Launcher.exe");
      byte[] adapterResource = target.Is64Bit()
        ? Resources.Bootstrapper_x64
        : Resources.Bootstrapper;
      var adapterPath = target.Is64Bit()
        ? Path.Combine(AppDataDir, "Bootstrapper_x64.dll")
        : Path.Combine(AppDataDir, "Bootstrapper.dll");

      // Get the .NET diver assembly to inject into the target process
      byte[] diverResource = Resources.ScubaDiver;
      diverPath = Path.Combine(AppDataDir, Resources.ScubaDiver_AsmName);

      // Check if injector or bootstrap resources differ from copies on disk
      OverrideFileIfChanged(launcherPath, launcherResource);
      OverrideFileIfChanged(adapterPath, adapterResource);
      OverrideFileIfChanged(diverPath, diverResource);
    }

    private static void OverrideFileIfChanged(string path, byte[] data)
    {
      string newDataHash = HashUtils.BufferSHA256(data);
      string existingDataHash = File.Exists(path)
        ? HashUtils.FileSHA256(path)
        : string.Empty;
      if (newDataHash != existingDataHash)
      {
        File.WriteAllBytes(path, data);
      }
    }
  }
}
