/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MSBuildTask = Microsoft.Build.Utilities.Task;

using JetBrains.Refasmer.Filters;


namespace MTGOSDK.MSBuild.Tasks;

public class GenerateReferenceAssemblies : MSBuildTask
{
  /// <summary>
  /// The path to the MTGO application directory.
  /// </summary>
  [Required]
  public string MTGOAppDir { get; set; } = string.Empty;

  /// <summary>
  /// The assembly version of the MTGO executable.
  /// </summary>
  [Required]
  public string Version { get; set; } = string.Empty;

  /// <summary>
  /// Whether the task has been skipped.
  /// </summary>
  [Output]
  public bool HasSkipped { get; set; } = false;

  /// <summary>
  /// The path to store the generated reference assemblies.
  /// </summary>
  [Required]
  [Output]
  public string OutputPath { get; set; } = string.Empty;

  public override bool Execute()
  {
    // Abort if reference assemblies for the current version already exist
    string versionPath = Path.Combine(OutputPath, Version);
    if (Directory.Exists(versionPath))
    {
      Log.LogMessage(MessageImportance.High,
          $"MTGOSDK.MSBuild: Reference assemblies for version {Version} already exist.");

      HasSkipped = true;
      OutputPath = versionPath;

      return true;
    }
    // Clear out previous versions' reference assemblies
    else if (Directory.Exists(OutputPath))
    {
      DirectoryInfo dir = new DirectoryInfo(OutputPath);
      foreach(FileInfo file in dir.GetFiles())
        file.Delete();
      foreach(DirectoryInfo subDirectory in dir.GetDirectories())
        subDirectory.Delete(true);
    }

    // Update the output path to include the version
    OutputPath = versionPath;
    Directory.CreateDirectory(OutputPath);

    // Generate new reference assemblies for the current version using Refasmer
    foreach(var filePath in Directory.GetFiles(MTGOAppDir)
      .Where(file => Regex.IsMatch(Path.GetExtension(file), @"\.(dll|exe)$")))
    {
      var fileName = Path.GetFileName(filePath);
      try
      {
        var asm = ReferenceAssemblyGenerator.Convert(filePath, new AllowAll());
        File.WriteAllBytes(Path.Combine(OutputPath, fileName), asm);
      }
      catch (InvalidOperationException e)
      {
        Log.LogMessage(MessageImportance.High,
            $"Encountered an error while parsing {fileName}: {e.Message}");
        return false;
      }
    }

    return true;
  }
}
