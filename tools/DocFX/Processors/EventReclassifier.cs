/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace DocProcessor.Processors;

public static class EventReclassifier
{
  private static readonly string[] EventTypePatterns = new[]
  {
    "MTGOSDK.Core.Reflection.Proxy.EventProxy",
    "MTGOSDK.Core.Reflection.Proxy.EventHookProxy",
    "MTGOSDK.Core.Reflection.EventHookWrapper"
  };

  // Compile regex for performance
  private static readonly Regex[] CompiledPatterns = Array.ConvertAll(EventTypePatterns,
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

    Console.WriteLine($"Reclassifying EventProxy/EventHookWrapper fields as Events...");
    Console.WriteLine($"Processing YAML files in: {yamlDirectory}");

    int filesModified = 0;
    var files = Directory.GetFiles(yamlDirectory, "*.yml", SearchOption.AllDirectories);

    // Process in parallel
    Parallel.ForEach(files, (file) =>
    {
      if (ProcessFile(file))
      {
        System.Threading.Interlocked.Increment(ref filesModified);
        Console.WriteLine($"  Modified: {Path.GetFileName(file)}");
      }
    });

    Console.WriteLine($"\nDone! Reclassified events in {filesModified} file(s)");
    return 0;
  }

  private static bool ProcessFile(string filePath)
  {
    string content = File.ReadAllText(filePath);

    // Fast check
    bool hasEventProxies = false;
    foreach (var pattern in CompiledPatterns)
    {
      if (pattern.IsMatch(content))
      {
        hasEventProxies = true;
        break;
      }
    }

    if (!hasEventProxies) return false;

    string[] lines = File.ReadAllLines(filePath);
    var newLines = new System.Collections.Generic.List<string>(lines.Length);
    bool modified = false;

    bool inItem = false;
    bool currentItemIsEventProxy = false;
    int typeLineIndex = -1;

    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i];

      if (line.Contains("- uid:"))
      {
        // Process previous item
        if (currentItemIsEventProxy && typeLineIndex >= 0)
        {
          newLines[typeLineIndex] = newLines[typeLineIndex].Replace("type: Field", "type: Event");
          modified = true;
        }

        inItem = true;
        currentItemIsEventProxy = false;
        typeLineIndex = -1;
      }

      if (inItem)
      {
        if (Regex.IsMatch(line, @"^\s+type: Field\s*$"))
        {
          typeLineIndex = newLines.Count;
        }

        if (Regex.IsMatch(line, @"^\s+type:"))
        {
          foreach (var pattern in CompiledPatterns)
          {
            if (pattern.IsMatch(line))
            {
              currentItemIsEventProxy = true;
              break;
            }
          }
        }
      }

      newLines.Add(line);
    }

    // Handle last item
    if (currentItemIsEventProxy && typeLineIndex >= 0)
    {
      newLines[typeLineIndex] = newLines[typeLineIndex].Replace("type: Field", "type: Event");
      modified = true;
    }

    if (modified)
    {
      File.WriteAllLines(filePath, newLines);
      return true;
    }

    return false;
  }
}
