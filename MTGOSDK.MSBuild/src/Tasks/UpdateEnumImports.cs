/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace MTGOSDK.MSBuild.Tasks;

/// <summary>
/// Refreshes enum import regions or reports stale regions during a check build.
/// </summary>
public sealed class UpdateEnumImports : MSBuildTask
{
  /// <summary>
  /// Source files to inspect for enum import regions.
  /// </summary>
  [Required]
  public ITaskItem[] SourceFiles { get; set; } = Array.Empty<ITaskItem>();

  /// <summary>
  /// Resolved assemblies containing the referenced enum types.
  /// </summary>
  public ITaskItem[] ReferencePaths { get; set; } = Array.Empty<ITaskItem>();

  /// <summary>
  /// Reports stale imports as errors without changing source files.
  /// </summary>
  public bool CheckOnly { get; set; }

  /// <summary>
  /// Source files changed by this task.
  /// </summary>
  [Output]
  public ITaskItem[] UpdatedFiles { get; set; } = Array.Empty<ITaskItem>();

  public override bool Execute()
  {
    try
    {
      UpdatedFiles = EnumRegionUpdater.Update(
        SourceFiles.Select(item => item.ItemSpec),
        ReferencePaths.Select(item => item.ItemSpec), CheckOnly)
        .Select(path => (ITaskItem)new TaskItem(path))
        .ToArray();

      foreach (var file in UpdatedFiles)
        Log.LogMessage(MessageImportance.High,
          $"MTGOSDK.MSBuild: Refreshed enum imports in {file.ItemSpec}");

      return true;
    }
    catch (EnumImportException error)
    {
      Log.LogError(null, error.Code, null, error.File, error.Line, 1, 0, 0, error.Message);
    }
    catch (Exception error)
    {
      Log.LogErrorFromException(error, showStackTrace: true);
    }

    return false;
  }
}
