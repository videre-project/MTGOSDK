/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace DocProcessor.Processors;

public static class MemberReorderer
{
  public static async Task<int> Execute(string[] args)
  {
    string? sourceRoot = null;
    string? yamlDirectory = null;

    for (int i = 1; i < args.Length; i++)
    {
      if (args[i] == "-SourceRoot" && i + 1 < args.Length)
        sourceRoot = args[i + 1];
      if (args[i] == "-YamlDirectory" && i + 1 < args.Length)
        yamlDirectory = args[i + 1];
    }

    if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrEmpty(yamlDirectory))
    {
      Console.WriteLine("Missing -SourceRoot or -YamlDirectory argument");
      return 1;
    }

    Console.WriteLine($"Building source order map from: {sourceRoot}");

    var sourceOrderMap = BuildSourceOrderMap(sourceRoot);

    Console.WriteLine($"Found member order for {sourceOrderMap.Count} types");
    Console.WriteLine($"\nProcessing YAML files in: {yamlDirectory}");

    int filesModified = 0;
    var files = Directory.GetFiles(yamlDirectory, "*.yml", SearchOption.AllDirectories);

    Parallel.ForEach(files, (file) =>
    {
      if (ReorderYamlChildren(file, sourceOrderMap))
      {
        System.Threading.Interlocked.Increment(ref filesModified);
        Console.WriteLine($"  Reordered: {Path.GetFileName(file)}");
      }
    });

    Console.WriteLine($"\nDone! Reordered members in {filesModified} file(s)");
    return 0;
  }

  private static ConcurrentDictionary<string, List<string>> BuildSourceOrderMap(string sourceRoot)
  {
    var map = new ConcurrentDictionary<string, List<string>>();
    var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);

    Parallel.ForEach(sourceFiles, (filePath) =>
    {
      var (fullTypeName, members) = GetMemberOrder(filePath);
      if (members != null && members.Count > 0)
      {
        // Dedupe
        var uniqueMembers = members.Distinct().ToList();
        map.TryAdd(fullTypeName, uniqueMembers);
      }
    });

    return map;
  }

  private static (string FullTypeName, List<string> Members) GetMemberOrder(string filePath)
  {
    string[] lines = File.ReadAllLines(filePath);
    string content = string.Join("\n", lines); // Not ideal for perf but needed for namespace regex match if multiline

    // Or just scan lines for namespace
    string ns = "";

    // Simple namespace extraction (assumes single line namespace declaration usually)
    foreach (var line in lines)
    {
      var match = Regex.Match(line, @"namespace\s+([\w.]+)\s*[;{]");
      if (match.Success)
      {
        ns = match.Groups[1].Value;
        break;
      }
    }

    string typeName = Path.GetFileNameWithoutExtension(filePath);
    string fullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";

    var members = new List<string>();

    var excludeKeywords = new HashSet<string> {
        "if", "while", "for", "foreach", "switch", "catch", "lock", "using",
        "class", "struct", "interface", "enum", "delegate", "get", "set", "new"
      };

    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i];
      if (string.IsNullOrWhiteSpace(line)) continue;
      if (Regex.IsMatch(line, @"^\s*(//|/\*|\*|#|\[|$)")) continue;
      if (Regex.IsMatch(line, @"^\s*(using|namespace)\s")) continue;

      string memberName = null!;

      // Match methods, properties, fields
      var methodExpr = Regex.Match(line, @"^\s*(public|private|protected|internal)\s+.+\s+(\w+)\s*\([^)]*\)\s*=>");
      if (methodExpr.Success) memberName = methodExpr.Groups[2].Value;

      else
      {
        var methodBody = Regex.Match(line, @"^\s*(public|private|protected|internal)\s+.+\s+(\w+)\s*\([^)]*\)\s*($|{)");
        if (methodBody.Success) memberName = methodBody.Groups[2].Value;

        else
        {
          var propExpr = Regex.Match(line, @"^\s*(public|private|protected|internal)\s+.+\s+(\w+)\s*=>");
          if (propExpr.Success) memberName = propExpr.Groups[2].Value;

          else
          {
            var propBody = Regex.Match(line, @"^\s*(public|private|protected|internal)\s+.+\s+(\w+)\s*{");
            if (propBody.Success) memberName = propBody.Groups[2].Value;

            else
            {
              var fieldInit = Regex.Match(line, @"^\s*(public|private|protected|internal)\s+.+\s+(\w+)\s*=(?!=)");
              if (fieldInit.Success) memberName = fieldInit.Groups[2].Value;

              else
              {
                var fieldSimple = Regex.Match(line, @"^\s*(public|private|protected|internal)\s+.+\s+(\w+)\s*;");
                if (fieldSimple.Success) memberName = fieldSimple.Groups[2].Value;
              }
            }
          }
        }
      }

      if (memberName != null && !excludeKeywords.Contains(memberName))
      {
        members.Add(memberName);
      }
    }

    return (fullTypeName, members);
  }

  private static bool ReorderYamlChildren(string yamlPath, ConcurrentDictionary<string, List<string>> sourceOrderMap)
  {
    string[] lines = File.ReadAllLines(yamlPath);

    bool inType = false;
    string typeFullName = "";
    int childrenStart = -1;
    int childrenEnd = -1;
    int childrenIndent = 0;
    var childrenItems = new List<(int LineIndex, string Content, string Uid)>();

    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i];

      if (!inType)
      {
        var uidMatch = Regex.Match(line, @"^- uid:\s*(.+)$");
        if (uidMatch.Success)
        {
          typeFullName = uidMatch.Groups[1].Value.Trim();
          inType = true;
          continue;
        }
      }

      if (inType && childrenStart == -1)
      {
        var childrenMatch = Regex.Match(line, @"^(\s*)children:\s*$");
        if (childrenMatch.Success)
        {
          childrenIndent = childrenMatch.Groups[1].Length;
          childrenStart = i + 1;
          continue;
        }
      }

      if (childrenStart > 0 && childrenEnd == -1)
      {
        var itemMatch = Regex.Match(line, @"^(\s*)- (.+)$");
        if (itemMatch.Success)
        {
          int entryIndent = itemMatch.Groups[1].Length;
          if (entryIndent >= childrenIndent)
          {
            childrenItems.Add((i, line, itemMatch.Groups[2].Value.Trim()));
            continue;
          }
        }
        childrenEnd = i;
        break;
      }
    }

    if (childrenStart == -1 || childrenItems.Count == 0) return false;

    if (!sourceOrderMap.TryGetValue(typeFullName, out var sourceOrder)) return false;

    var orderMap = new Dictionary<string, int>();
    for (int i = 0; i < sourceOrder.Count; i++) orderMap[sourceOrder[i]] = i;

    var sortedChildren = childrenItems.OrderBy(item =>
    {
      string uid = item.Uid;
      string memberName = uid.Split('.').Last();

      if (memberName.StartsWith("#ctor")) memberName = "#ctor";
      else if (memberName.Contains("(")) memberName = memberName.Split('(')[0];
      else if (memberName.Contains("``")) memberName = memberName.Split(new[] { "``" }, StringSplitOptions.None)[0];

      return orderMap.ContainsKey(memberName) ? orderMap[memberName] : 999;
    }).ToList();

    // Check if changed
    bool changed = false;
    for (int i = 0; i < childrenItems.Count; i++)
    {
      if (childrenItems[i].Uid != sortedChildren[i].Uid)
      {
        changed = true;
        break;
      }
    }

    if (!changed) return false;

    // Rebuild
    var newLines = new List<string>();
    for (int i = 0; i < childrenStart; i++) newLines.Add(lines[i]);
    foreach (var child in sortedChildren) newLines.Add(child.Content);

    // If childrenEnd was set (we broke loop), add remaining
    // If childrenEnd was NOT set (loop finished), all remaining lines were inside block?? No, logic above handles break.
    // If childrenEnd is -1, it implies end of file or loop finished.
    if (childrenEnd == -1) childrenEnd = lines.Length;

    for (int i = childrenEnd; i < lines.Length; i++) newLines.Add(lines[i]);

    File.WriteAllLines(yamlPath, newLines);
    return true;
  }
}
