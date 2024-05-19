/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.IO;

using MTGOSDK.Win32.Extensions;
using MTGOSDK.Win32.Injection;


namespace MTGOSDK.Resources;

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
