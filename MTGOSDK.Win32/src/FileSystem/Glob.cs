/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;


namespace MTGOSDK.Win32.FileSystem;

public class Glob
{
  public string[] Matches = new string[] { Environment.CurrentDirectory };

  public static implicit operator string[]?(Glob? glob) =>
    glob?.Matches;
  public static implicit operator string?(Glob? glob) =>
    glob?.Matches
      .OrderByDescending(f => new DirectoryInfo(f).LastWriteTime)
      .FirstOrDefault();

  public static Regex ParseGlob(string pattern) =>
    new(
      "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
      RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

  public Glob(string directory)
  {
    // Normalize path separators to facilitate splitting.
    string normalizedPath = directory.Replace('\\', '/');
    string[] patterns = normalizedPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    // If the path was rooted, re-add the root segment correctly.
    if (directory.StartsWith("/") || directory.StartsWith("\\"))
    {
      Matches = new string[] { "/" };
    }
    else if (normalizedPath.Contains(":") && Path.IsPathRooted(directory))
    {
       string drive = normalizedPath.Split(':')[0] + ":/";
       Matches = new string[] { drive };
       // Skip the drive segment in patterns if it was parsed as a root.
       if (patterns.Length > 0 && patterns[0].Contains(":"))
       {
           patterns = patterns.Skip(1).ToArray();
       }
    }

    foreach (string pattern in patterns)
    {
      // Handle relative parent directory pattern as a special case.
      if (pattern == "..")
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
          .Where(p => Directory.Exists(p))
          .SelectMany(basePath =>
            // Query the filesystem for files and directories in the current path.
            new DirectoryInfo(basePath)
              .EnumerateFileSystemInfos()
              // Evaluate the glob pattern against each file and directory.
              .Where(p =>
              {
                string relativePath = p.FullName.Replace('\\', '/');
                string normalizedBase = basePath.Replace('\\', '/');
                if (relativePath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                  relativePath = relativePath.Substring(normalizedBase.Length).TrimStart('/');
                }

                return pattern.Contains("*") || pattern.Contains("?")
                  // If the glob pattern contains any wildcards, use Regex to match.
                  ? ParseGlob(pattern).IsMatch(relativePath)
                  // Otherwise, match the glob pattern as literals.
                  : relativePath.Equals(pattern, StringComparison.OrdinalIgnoreCase);
              }))
          .Select(f => f.FullName)
          .ToArray();
      }
    }
  }
}
