using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

using RemoteNET.Internal.Extensions;
using RemoteNET.Properties;


namespace RemoteNET
{
  public static class Bootstrapper
  {
    public static string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        typeof(RemoteApp).Assembly.GetName().Name);

    public static void Inject(Process target, ushort diverPort)
    {
      // Not injected yet, Injecting adapter now (which should load the Diver)
      string targetDotNetVer = target.GetSupportedTargetFramework();
      GetInjectionToolkit(target,
          out string launcherPath,
          out string scubaDiverDllPath);
      string adapterExecutionArg = string.Join("*",
          scubaDiverDllPath,
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
        // _ = injectorProc.StandardOutput.ReadToEnd();
        var stdout = injectorProc.StandardOutput.ReadToEnd();
        Console.WriteLine("Injector stdout: " + stdout);
      }
    }

    private static void GetInjectionToolkit(
      Process target,
      out string launcherPath,
      out string scubaDiverDllPath)
    {
      // Dumping injector + adapter DLL to a %localappdata%\RemoteNET
      DirectoryInfo remoteNetAppDataDirInfo = new DirectoryInfo(AppDataDir);
      if (!remoteNetAppDataDirInfo.Exists)
        remoteNetAppDataDirInfo.Create();

      // Decide which injection toolkit to use x32 or x64
      byte[] launcherResource = Resources.Launcher;
      launcherPath = Path.Combine(AppDataDir, "Launcher.exe");
      byte[] adapterResource = Resources.Bootstrapper;
      var adapterPath = Path.Combine(AppDataDir, "Bootstrapper.dll");
      if (target.Is64Bit())
      {
        launcherResource = Resources.Launcher_x64;
        launcherPath = Path.Combine(AppDataDir, "Launcher_x64.exe");
        adapterResource = Resources.Bootstrapper_x64;
        adapterPath = Path.Combine(AppDataDir, "Bootstrapper_x64.dll");
      }

      // Check if injector or bootstrap resources differ from copies on disk
      OverrideFileIfChanged(launcherPath, launcherResource);
      OverrideFileIfChanged(adapterPath, adapterResource);

      // Unzip scuba diver and dependencies into their own directory
      string targetDiver = "ScubaDiver_NetFramework";
      var scubaDestDirInfo = new DirectoryInfo(Path.Combine(AppDataDir, targetDiver));
      if (!scubaDestDirInfo.Exists)
      {
        scubaDestDirInfo.Create();
      }

      // Temp dir to dump to before moving to app data (where it might have previously deployed files
      // AND they might be in use by some application so they can't be overwritten)
      Random rand = new Random();
      var tempDir = Path.Combine(
        Path.GetTempPath(),
        rand.Next(100000).ToString());
      DirectoryInfo tempDirInfo = new DirectoryInfo(tempDir);
      if (tempDirInfo.Exists)
      {
        tempDirInfo.Delete(recursive: true);
      }
      tempDirInfo.Create();
      using (var diverZipMemoryStream = new MemoryStream(Resources.ScubaDivers))
      {
        ZipArchive diverZip = new ZipArchive(diverZipMemoryStream);
        // This extracts the "Scuba" directory from the zip to *tempDir*
        diverZip.ExtractToDirectory(tempDir);
      }

      // Going over unzipped files and checking which of those we need to copy to our AppData directory
      tempDirInfo = new DirectoryInfo(Path.Combine(tempDir, "ScubaDiver"));
      foreach (FileInfo fileInfo in tempDirInfo.GetFiles())
      {
        string destPath = Path.Combine(scubaDestDirInfo.FullName, fileInfo.Name);
        if (File.Exists(destPath))
        {
          string dumpedFileHash = HashUtils.FileSHA256(fileInfo.FullName);
          string previousFileHash = HashUtils.FileSHA256(destPath);
          if (dumpedFileHash == previousFileHash)
          {
            // Skipping file because the previous version of it has the same hash
            continue;
          }
        }
        // Moving file to our AppData directory
        File.Delete(destPath);
        fileInfo.MoveTo(destPath);
      }

      // We are done with our temp directory
      tempDirInfo.Delete(recursive: true);
      var matches = scubaDestDirInfo
        .EnumerateFiles()
        .Where(scubaFile => scubaFile.Name.EndsWith($"{targetDiver}.dll"));

      scubaDiverDllPath = matches.Single().FullName;
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
