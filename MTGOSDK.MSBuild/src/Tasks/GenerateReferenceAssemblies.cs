/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;
using JetBrains.Refasmer.Filters;

using FileSystem;
using ReferenceAssembly;


namespace MTGOSDK.MSBuild.Tasks;

public class GenerateReferenceAssemblies : Task
{
  /// <summary>
  ///  The path to the MTGO application directory.
  /// </summary>
  [Required]
  [Output]
  public string MTGOAppDir { get; set; } = string.Empty;

  /// <summary>
  ///  The path to the MTGO user data directory.
  /// </summary>
  [Output]
  public string MTGODataDir { get; set; } = string.Empty;

  /// <summary>
  /// The path to store the generated reference assemblies.
  /// </summary>
  ///
  [Required]
  public string OutputPath { get; set; } = string.Empty;

  /// <summary>
  /// The assembly version of the MTGO executable.
  /// </summary>
  [Output]
  public string Version { get; set; } = string.Empty;

  public override bool Execute()
  {
    // Expand any glob patterns in the input paths
    MTGOAppDir = new Glob(MTGOAppDir);
    MTGODataDir = new Glob(MTGODataDir);

    // Get assembly version for the MTGO executable
    string MTGOExePath = Path.Combine(MTGOAppDir, "MTGO.exe");
    Version = Assembly.LoadFile(MTGOExePath).GetName().Version.ToString();

    // Abort if reference assemblies for the current version already exist
    if (File.Exists(OutputPath))
    {
      Log.LogMessage(MessageImportance.High,
          $"Reference assemblies for version {Version} already exist.");
      return true;
    }
    else
    {
      Directory.CreateDirectory(OutputPath);
    }

    // Generate new reference assemblies for the current version using Refasmer
    try
    {
      foreach(var filePath in Directory.GetFiles(MTGOAppDir)
        .Where(file => Regex.IsMatch(Path.GetExtension(file), @"\.(dll|exe)$")))
      {
        var fileName = Path.GetFileName(filePath);
        var data = ReferenceAssemblyGenerator.Convert(filePath, new AllowAll());
        File.WriteAllBytes(Path.Combine(OutputPath, fileName), data);
      }
    }
    catch (Exception ex)
    {
      Log.LogErrorFromException(ex);
      return false;
    }

    return true;
  }
}
