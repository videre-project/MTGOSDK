/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Linq;
using System.Reflection;
using System.Xml;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

using MTGOSDK.Win32.Utilities.FileSystem;
using static MTGOSDK.Win32.Constants;


namespace MTGOSDK.MSBuild.Tasks;

public class ExtractMTGOInstallation : Task
{
  /// <summary>
  /// The path to the MTGO application directory.
  /// </summary>
  [Required]
  [Output]
  public string MTGOAppDir { get; set; } = string.Empty;

  /// <summary>
  /// The path to the MTGO user data directory.
  /// </summary>
  [Output]
  public string MTGODataDir { get; set; } = string.Empty;

  /// <summary>
  /// The reference paths to extract from the manifest.
  /// </summary>
  public string[] ReferencePaths { get; set; } = Array.Empty<string>();

  /// <summary>
  /// The assembly version of the MTGO executable.
  /// </summary>
  [Output]
  public string Version { get; set; } = string.Empty;

  public override bool Execute()
  {
    // Try to expand any glob patterns in the input paths
    MTGOAppDir = new Glob(MTGOAppDir);
    MTGODataDir = new Glob(MTGODataDir);

    if (MTGOAppDir is not null && Directory.Exists(MTGOAppDir))
    {
      // Get assembly version for the MTGO executable
      string MTGOExePath = Path.Combine(MTGOAppDir, "MTGO.exe");
      Version = Assembly.LoadFile(MTGOExePath).GetName().Version.ToString();

      return true;
    }

    var deploymentManifest = new XmlDocument();
    deploymentManifest.Load(ApplicationUri);
    var rootUrl = ApplicationUri.Substring(0, ApplicationUri.LastIndexOf('/'));

    // Extract the MTGO executable manifest uri from the deployment manifest.
    var asms = deploymentManifest.GetElementsByTagName("dependentAssembly");
    var manifestUri = asms[0].Attributes["codebase"].Value;

    // Extract the MTGO version from the deployment manifest.
    var identities = deploymentManifest.GetElementsByTagName("assemblyIdentity");
    Version = identities[0].Attributes["version"].Value;

    // Determine the MTGO codebase version for extraction.
    var codebase = manifestUri.Substring(0, manifestUri.LastIndexOf('\\'));
    var codebaseDir = $"MTGO_{codebase}";
    MTGOAppDir = Path.Combine(Path.GetTempPath(), codebaseDir);

    if (Directory.Exists(MTGOAppDir))
    {
      Log.LogMessage(MessageImportance.High, $"Using cached v{Version} at {MTGOAppDir}");
      return true;
    }

    // Otherwise, create a temporary MTGO application directory.
    Log.LogMessage(MessageImportance.High, $"Extracting MTGO v{Version} to {MTGOAppDir}");
    Directory.CreateDirectory(MTGOAppDir);

    // Extract the MTGO assemblies from the application manifest.
    XmlDocument manifest = new();
    manifestUri = $"{rootUrl}/{manifestUri}".Replace('\\', '/');
    manifest.Load(manifestUri);

    var assemblies = manifest.GetElementsByTagName("dependentAssembly")
      .Cast<XmlElement>()
      .Where(asm => asm.Attributes["dependencyType"].Value == "install")
      .Select(asm => asm.Attributes["codebase"].Value.Replace('\\', '/'))
      .Where(name =>
        ReferencePaths.Length == 0 || ReferencePaths.Contains(name))
      .Select(name => {
        var url = $"{rootUrl}/{codebase}/{name}";
        var path = Path.Combine(MTGOAppDir, name);
        return (url, path);
      });

    // Include the source deployment and application manifests.
    var files = (new List<string> { ApplicationUri, manifestUri })
      .Select(url => {
        var name = url.Substring(url.LastIndexOf('/') + 1);
        var path = Path.Combine(MTGOAppDir, name);
        return (url, path);
      })
      .Concat(assemblies)
      .OrderBy(t => Path.GetFileName(t.Item2));

    return DownloadFilesAsync(files).Result;
  }

  /// <summary>
  /// Download files from an enumerable of (url, path) tuples.
  /// </summary>
  /// <param name="files">
  /// An enumerable of (url, path) tuples to download.
  /// </param>
  /// <returns>
  /// True if all files were downloaded successfully, false otherwise.
  /// </returns>
  public async Task<bool> DownloadFilesAsync(IEnumerable<(string, string)> files)
  {
    using (var client = new HttpClient())
    {
      foreach((string url, string path) in files)
      {
        var name = Path.GetFileName(path);
        try
        {
          using var s = await client.GetStreamAsync(url);
          using var fs = new FileStream(path, FileMode.Create);
          await s.CopyToAsync(fs);

          Log.LogMessage(MessageImportance.High, $"--> Extracted {name}");
        }
        catch (Exception e)
        {
          Log.LogMessage(MessageImportance.High, $"Failed to download {name} from {url}");
          Log.LogErrorFromException(e);
          return false;
        }
      }
    }

    return true;
  }
}
