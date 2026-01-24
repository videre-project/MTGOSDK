/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace DocProcessor.Processors;

public static class InheritedMemberRemover
{
  private static readonly string[] PatternsToRemove = new[]
  {
    "MTGOSDK.Core.Reflection.DLRWrapper",
    "MTGOSDK.Core.Reflection.Serialization.SerializableBase"
  };

  private static readonly Regex[] CompiledPatterns = Array.ConvertAll(PatternsToRemove,
    p => new Regex(Regex.Escape(p), RegexOptions.Compiled));

  public static async Task<int> Execute(string[] args)
  {
  string? yamlDirectory = null;

  for (int i = 1; i < args.Length; i++)
  {
    if (args[i] == "-YamlDirectory" && i + 1 < args.Length)
    yamlDirectory = args[i + 1];
  }

  if (string.IsNullOrEmpty(yamlDirectory))
  {
    Console.WriteLine("Missing -YamlDirectory argument");
    return 1;
  }

  Console.WriteLine($"Processing YAML files in: {yamlDirectory}");
  Console.WriteLine($"Removing inherited members from: {string.Join(", ", PatternsToRemove)}");

  int filesModified = 0;
  var files = Directory.GetFiles(yamlDirectory, "*.yml", SearchOption.AllDirectories);

  Parallel.ForEach(files, (file) =>
  {
    if (ProcessFile(file))
    {
    System.Threading.Interlocked.Increment(ref filesModified);
    Console.WriteLine($"  Modified: {Path.GetFileName(file)}");
    }
  });

  Console.WriteLine($"\nDone! Modified {filesModified} file(s)");
  return 0;
  }

  private static bool ProcessFile(string filePath)
  {
  string content = File.ReadAllText(filePath);
  if (!content.Contains("inheritedMembers:"))
  {
    return false;
  }

  string[] lines = File.ReadAllLines(filePath);
  var newLines = new List<string>(lines.Length);
  bool inInheritedMembers = false;
  int inheritedMembersIndent = 0;
  var currentInheritedMembers = new List<string>();

  for (int i = 0; i < lines.Length; i++)
  {
    string line = lines[i];

    // Detect start of inheritedMembers section
    var match = Regex.Match(line, @"^(\s*)inheritedMembers:\s*$");
    if (match.Success)
    {
    inInheritedMembers = true;
    inheritedMembersIndent = match.Groups[1].Length;
    currentInheritedMembers.Clear();
    continue;
    }

    if (inInheritedMembers)
    {
    // Check if we've exited the inheritedMembers section
    // Match indentation: if line has non-whitespace but isn't a list item at correct indent
    var contentMatch = Regex.Match(line, @"^(\s*)\S");
    bool isListItem = Regex.IsMatch(line, @"^\s*-\s");

    if (contentMatch.Success && !isListItem)
    {
      int currentIndent = contentMatch.Groups[1].Length;
      if (currentIndent <= inheritedMembersIndent)
      {
      // End of section - filter and flush
      FlushInheritedMembers(newLines, currentInheritedMembers, inheritedMembersIndent);

      inInheritedMembers = false;
      newLines.Add(line);
      continue;
      }
    }

    // Collect inheritedMembers entries
    if (isListItem)
    {
      currentInheritedMembers.Add(line);
    }
    continue;
    }

    newLines.Add(line);
  }

  // Handle case where inheritedMembers is at end of file
  if (inInheritedMembers)
  {
    FlushInheritedMembers(newLines, currentInheritedMembers, inheritedMembersIndent);
  }

  // Check if content changed needs robust check
  // We can check if line count differs or do full string compare
  // Since we rebuild the list, simple count check might be enough if we only remove lines
  if (newLines.Count != lines.Length)
  {
    File.WriteAllLines(filePath, newLines);
    return true;
  }

  // If count matches, check content (rare case where we remove but line count same?)
  // Actually just write if any line matches were found
  // Let's implement robust check
  bool changed = false;
  if (newLines.Count != lines.Length) changed = true;
  else
  {
    for (int i = 0; i < lines.Length; i++)
    {
    if (lines[i] != newLines[i])
    {
      changed = true;
      break;
    }
    }
  }

  if (changed)
  {
    File.WriteAllLines(filePath, newLines);
    return true;
  }

  return false;
  }

  private static void FlushInheritedMembers(List<string> output, List<string> members, int indentLevel)
  {
  var filtered = new List<string>();
  foreach (var member in members)
  {
    bool shouldKeep = true;
    foreach (var pattern in CompiledPatterns)
    {
    if (pattern.IsMatch(member))
    {
      shouldKeep = false;
      break;
    }
    }
    if (shouldKeep) filtered.Add(member);
  }

  if (filtered.Count > 0)
  {
    string indent = new string(' ', indentLevel);
    output.Add($"{indent}inheritedMembers:");
    output.AddRange(filtered);
  }
  }
}
