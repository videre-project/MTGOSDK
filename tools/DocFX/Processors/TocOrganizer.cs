/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DocProcessor.Processors;

public static class TocOrganizer
{
  public static async Task<int> Execute(string[] args)
  {
    string? sourceRoot = null;
    string? tocPath = null;

    for (int i = 1; i < args.Length; i++)
    {
      if (args[i] == "-SourceRoot" && i + 1 < args.Length)
        sourceRoot = args[i + 1];
      if (args[i] == "-TocPath" && i + 1 < args.Length)
        tocPath = args[i + 1];
    }

    if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrEmpty(tocPath))
    {
      Console.WriteLine("Missing -SourceRoot or -TocPath argument");
      return 1;
    }

    Console.WriteLine($"Scanning source structure from: {sourceRoot}");
    Console.WriteLine($"TOC file: {tocPath}");

    var typeMap = GetTypeToFolderMap(sourceRoot);
    Console.WriteLine($"Found {typeMap.Count} types");

    var namespacesWithCategories = GetNamespacesWithCategories(typeMap);
    Console.WriteLine("\nNamespaces with organizational folders:");

    foreach (var ns in namespacesWithCategories.Keys.OrderBy(k => k))
    {
      var cats = string.Join(", ", namespacesWithCategories[ns].OrderBy(k => k));
      Console.WriteLine($"  {ns} : {cats}");
    }

    if (namespacesWithCategories.Count == 0)
    {
      Console.WriteLine("\nNo organizational folders found. Exiting.");
      return 0;
    }

    // Read TOC once
    var tocLines = File.ReadAllLines(tocPath).ToList();

    // Process each namespace
    foreach (var ns in namespacesWithCategories.Keys)
    {
      Console.WriteLine($"\nReorganizing: {ns}");
      tocLines = ReorganizeNamespaceToc(tocLines, ns, typeMap);
    }

    Console.WriteLine($"\nWriting modified TOC...");
    File.WriteAllLines(tocPath, tocLines);
    Console.WriteLine("Done!");

    return 0;
  }

  private class TypeInfo
  {
    public string Namespace { get; set; } = "";
    public string Category { get; set; } = "";
  }

  private static Dictionary<string, TypeInfo> GetTypeToFolderMap(string sourceRoot)
  {
    var map = new Dictionary<string, TypeInfo>();
    var sourceFullPath = Path.GetFullPath(sourceRoot);
    var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);

    foreach (var filePath in sourceFiles)
    {
      string content = File.ReadAllText(filePath);
      var match = Regex.Match(content, @"namespace\s+([\w.]+)\s*[;{]");
      if (!match.Success) continue;

      string ns = match.Groups[1].Value;
      string typeName = Path.GetFileNameWithoutExtension(filePath);
      string fullTypeName = $"{ns}.{typeName}";

      string relativePath = (Path.GetDirectoryName(filePath) ?? "").Replace(sourceFullPath, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var folderParts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

      string expectedNamespace = "MTGOSDK";
      if (folderParts.Length > 0)
      {
        expectedNamespace = "MTGOSDK." + string.Join(".", folderParts);
      }

      string category = "";
      if (ns != expectedNamespace && folderParts.Length > 0)
      {
        category = folderParts[folderParts.Length - 1];
      }

      map[fullTypeName] = new TypeInfo
      {
        Namespace = ns,
        Category = category
      };
    }

    return map;
  }

  private static Dictionary<string, HashSet<string>> GetNamespacesWithCategories(Dictionary<string, TypeInfo> typeMap)
  {
    var result = new Dictionary<string, HashSet<string>>();

    foreach (var kvp in typeMap)
    {
      var info = kvp.Value;
      if (!string.IsNullOrEmpty(info.Category) && info.Namespace != null)
      {
        if (!result.ContainsKey(info.Namespace))
          result[info.Namespace] = new HashSet<string>();

        result[info.Namespace].Add(info.Category);
      }
    }

    return result;
  }

  private class TocItem
  {
    public List<string> Lines { get; set; } = new List<string>();
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
  }

  private static List<string> ReorganizeNamespaceToc(List<string> tocLines, string ns, Dictionary<string, TypeInfo> typeMap)
  {
    string escapedNs = Regex.Escape(ns);
    bool inNamespace = false;
    bool inItems = false;
    int namespaceIndentLen = 0;
    int startLine = -1;
    int endLine = -1;

    for (int i = 0; i < tocLines.Count; i++)
    {
      string line = tocLines[i];

      if (Regex.IsMatch(line, $@"^(\s*)- uid: {escapedNs}$"))
      {
        inNamespace = true;
        namespaceIndentLen = Regex.Match(line, @"^(\s*)").Length;
        continue;
      }

      if (inNamespace && !inItems)
      {
        if (Regex.IsMatch(line, @"^(\s*)items:\s*$"))
        {
          inItems = true;
          startLine = i + 1;
        }
        continue;
      }

      if (inItems)
      {
        var indentMatch = Regex.Match(line, @"^(\s*)- ");
        if (indentMatch.Success && indentMatch.Groups[1].Length <= namespaceIndentLen)
        {
          endLine = i - 1;
          break;
        }
      }
    }

    if (startLine == -1)
    {
      Console.WriteLine("  Warning: Could not find namespace in TOC");
      return tocLines;
    }

    if (endLine == -1) endLine = tocLines.Count - 1;

    Console.WriteLine($"  Found items at lines {startLine} to {endLine}");

    // Parse items
    var items = new List<TocItem>();
    TocItem? currentItem = null;
    string itemIndent = "";

    for (int i = startLine; i <= endLine; i++)
    {
      string line = tocLines[i];

      var uidMatch = Regex.Match(line, @"^(\s*)- uid: ");
      if (uidMatch.Success)
      {
        if (currentItem != null) items.Add(currentItem);
        itemIndent = uidMatch.Groups[1].Value;
        currentItem = new TocItem();
        currentItem.Lines.Add(line);

        var uidVal = Regex.Match(line, @"uid:\s*(.+)$");
        if (uidVal.Success) currentItem.Uid = uidVal.Groups[1].Value.Trim();
      }
      else if (currentItem != null && Regex.IsMatch(line, @"^\s+"))
      {
        currentItem.Lines.Add(line);
        var nameVal = Regex.Match(line, @"name:\s*(.+)$");
        if (nameVal.Success) currentItem.Name = nameVal.Groups[1].Value.Trim();
      }
    }
    if (currentItem != null) items.Add(currentItem);

    Console.WriteLine($"  Parsed {items.Count} items");

    // Categorize
    var categories = new Dictionary<string, List<TocItem>>();
    var uncategorized = new List<TocItem>();

    foreach (var item in items)
    {
      string uid = item.Uid;
      string? category = null;

      if (typeMap.TryGetValue(uid, out var info) && info.Namespace == ns && !string.IsNullOrEmpty(info.Category))
      {
        category = info.Category;
      }
      else if (uid.StartsWith(ns + "."))
      {
        string localPart = uid.Substring(ns.Length + 1);
        if (localPart.Contains("."))
        {
          string parentTypeName = localPart.Split('.')[0];
          string parentUid = $"{ns}.{parentTypeName}";
          if (typeMap.TryGetValue(parentUid, out var pInfo) && pInfo.Namespace == ns && !string.IsNullOrEmpty(pInfo.Category))
          {
            category = pInfo.Category;
          }
        }
      }

      if (category != null)
      {
        if (!categories.ContainsKey(category)) categories[category] = new List<TocItem>();
        categories[category].Add(item);
      }
      else
      {
        uncategorized.Add(item);
      }
    }

    Console.WriteLine($"  Categories: {string.Join(", ", categories.Keys)}");
    Console.WriteLine($"  Uncategorized: {uncategorized.Count}");

    if (categories.Count == 0)
    {
      Console.WriteLine("  No categories found, skipping");
      return tocLines;
    }

    // Build new section
    var newSection = new List<string>();
    foreach (var category in categories.Keys.OrderBy(k => k))
    {
      newSection.Add($"{itemIndent}- name: {category}");
      newSection.Add($"{itemIndent}  items:");

      foreach (var item in categories[category].OrderBy(x => x.Name))
      {
        foreach (var l in item.Lines)
        {
          newSection.Add("  " + l);
        }
      }
    }

    foreach (var item in uncategorized) newSection.AddRange(item.Lines);

    // Rebuild
    var result = new List<string>();
    if (startLine > 0)
    {
      for (int i = 0; i < startLine; i++) result.Add(tocLines[i]);
    }
    result.AddRange(newSection);
    if (endLine < tocLines.Count - 1)
    {
      for (int i = endLine + 1; i < tocLines.Count; i++) result.Add(tocLines[i]);
    }

    return result;
  }
}
