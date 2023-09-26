/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.RegularExpressions;


namespace FileSystem;

public class Glob
{
  public string[] Matches = new string[] { Environment.CurrentDirectory };

  public static implicit operator string[](Glob glob) =>
    glob.Matches;
  public static implicit operator string(Glob glob) =>
    glob.Matches
      .OrderByDescending(f => new DirectoryInfo(f).LastWriteTime)
      .FirstOrDefault();

  public static Regex ParseGlob(string pattern) =>
    new(
      "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
      RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

  public Glob(string directory)
  {
    // foreach (string pattern in directory.Replace("/", @"\").Split(@"\"))
    foreach (string pattern in directory.Replace("/", @"\").Split(new char[] { '\\' }))
    {
      // Skip if pattern is an absolute path.
      if (Path.IsPathRooted(pattern))
      {
        Matches = new string[] {
          pattern.Contains(":")
            ? pattern + Path.DirectorySeparatorChar
            : pattern
        };
      }
      // Handle relative parent directory pattern as a special case.
      else if (pattern == "..")
      {
        Matches = Matches
          .Select(p => new DirectoryInfo(p).Parent.FullName)
          .ToArray();
      }
      // Handle double wildcard pattern as a special case.
      else if (pattern == "**")
      {
        Matches = Matches
          .Concat(Matches
            .SelectMany(basePath => Directory
                .GetDirectories(basePath, "*", SearchOption.AllDirectories)
                .Select(d => new DirectoryInfo(d).Parent.FullName)
                .Distinct()))
          .ToArray();
      }
      // Resolve the current pattern against all base filepaths.
      else
      {
        Matches = Matches
          .SelectMany(basePath =>
            // Query the filesystem for files and directories in the current path.
            new DirectoryInfo(basePath)
              .EnumerateFileSystemInfos()
              // Evaluate the glob pattern against each file and directory.
              .Where(p => pattern.Contains("*") || pattern.Contains("?")
                // If the glob pattern contains any wildcards, use Regex to match.
                ? ParseGlob(pattern)
                    .IsMatch(p.FullName.Substring(basePath.Length + 1))
                // Otherwise, match the glob pattern as literals.
                : p.FullName == Path.Combine(basePath, pattern)))
          .Select(f => f.FullName)
          .ToArray();
      }
    }
  }
}
