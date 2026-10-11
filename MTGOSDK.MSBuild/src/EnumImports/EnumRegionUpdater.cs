/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MTGOSDK.MSBuild;

/// <summary>
/// Updates marked source regions with enum members read from the referenced
/// client assemblies.
/// </summary>
public static class EnumRegionUpdater
{
  private static readonly Regex Marker = new(
    @"^(?<indent>[ \t]*)//#pragma\s+import\s+enum\s+(?<type>\S+)\s+(?<kind>begin|end)[ \t]*$",
    RegexOptions.CultureInvariant);
  private static readonly Regex ImportPrefix = new(
    @"^[ \t]*//#pragma\s+import\b", RegexOptions.CultureInvariant);

  public static IReadOnlyList<string> Update(
    IEnumerable<string> files, IEnumerable<string> references, bool checkOnly = false)
  {
    // Validate every region and resolve every enum before changing any file.
    var documents = files.Select(Path.GetFullPath).Distinct().Select(Read)
      .Where(document => document.Regions.Count != 0).ToList();
    if (documents.Count == 0)
      return Array.Empty<string>();

    var imports = documents.SelectMany(document => document.Regions).ToList();
    IReadOnlyDictionary<string, string> members;
    try
    {
      members = EnumMetadata.ReadMembers(references, imports.Select(region => region.Type));
    }
    catch (InvalidOperationException error)
    {
      var region = error is EnumMetadataException metadata
        ? imports.First(import => import.Type == metadata.TypeName) : imports[0];
      throw new EnumImportException("MTGO002", region.File, region.Line, error.Message);
    }

    var updates = new List<(Document Document, byte[] Bytes)>();
    foreach (var document in documents)
    {
      var text = document.Text;

      // Replace from the end so original offsets remain valid.
      foreach (var region in document.Regions.AsEnumerable().Reverse())
      {
        var lines = members[region.Type];
        var replacement = lines.Length == 0 ? "" : string.Join(region.Newline,
          lines.Split('\n').Select(line => region.Indent + line)) + region.Newline;
        text = text.Remove(region.Start, region.Length).Insert(region.Start, replacement);
      }

      if (text == document.Text)
        continue;
      if (checkOnly)
        throw new EnumImportException("MTGO003", document.Path, document.Regions[0].Line,
          "Enum import region is stale. Build without CheckImportedEnums=true to refresh it.");

      var bytes = document.Preamble.Concat(document.Encoding.GetBytes(text)).ToArray();
      updates.Add((document, bytes));
    }

    // An editor may have changed the file while its imports were being resolved.
    foreach (var update in updates)
      if (!File.ReadAllBytes(update.Document.Path).SequenceEqual(update.Document.Bytes))
        throw new EnumImportException("MTGO004", update.Document.Path, 1,
          "Source changed during enum import; retry the build.");

    foreach (var update in updates)
    {
      var temp = update.Document.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try
      {
        File.WriteAllBytes(temp, update.Bytes);
        File.Replace(temp, update.Document.Path, null);
      }
      finally
      {
        if (File.Exists(temp))
          File.Delete(temp);
      }
    }

    return updates.Select(update => update.Document.Path).ToArray();
  }

  private static Document Read(string path)
  {
    var bytes = File.ReadAllBytes(path);

    // Preserve BOM and encoding, including UTF-16/32 sources.
    var encoding = new Encoding[]
    {
      new UTF32Encoding(false, true, true),
      new UTF32Encoding(true, true, true),
      new UTF8Encoding(true, true),
      new UnicodeEncoding(false, true, true),
      new UnicodeEncoding(true, true, true)
    }.FirstOrDefault(candidate => bytes.Take(candidate.GetPreamble().Length)
      .SequenceEqual(candidate.GetPreamble())) ?? new UTF8Encoding(false, true);
    var preamble = encoding.GetPreamble();
    var text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);

    var regions = new List<Region>();
    Region? open = null;
    foreach (var comment in SourceComments.Standalone(text))
    {
      var line = text.Substring(comment.Start, comment.Length);
      if (!ImportPrefix.IsMatch(line))
        continue;

      var match = Marker.Match(line);
      if (!match.Success)
        throw Error(path, comment.Line, "Expected '//#pragma import enum <metadata type> begin|end'.");

      var type = match.Groups["type"].Value;
      if (type.StartsWith("global::", StringComparison.Ordinal))
        type = type.Substring(8);

      if (match.Groups["kind"].Value == "begin")
      {
        if (open is not null)
          throw Error(path, comment.Line, "Enum import regions cannot be nested.");
        if (comment.Newline.Length == 0)
          throw Error(path, comment.Line, "Enum import begin marker must end with a newline.");
        open = new Region(path, comment.Line, type, match.Groups["indent"].Value,
          comment.Newline, comment.Start + comment.Length + comment.Newline.Length);
      }
      else
      {
        if (open is null)
          throw Error(path, comment.Line, "Enum import end marker has no begin marker.");
        if (open.Type != type)
          throw Error(path, comment.Line, "Enum import begin and end types differ.");

        open.Length = comment.Start - open.Start;
        regions.Add(open);
        open = null;
      }
    }

    if (open is not null)
      throw Error(path, open.Line, "Enum import begin marker has no end marker.");

    return new Document(path, bytes, text, encoding, preamble, regions);
  }

  private static EnumImportException Error(string path, int line, string message) =>
    new("MTGO001", path, line, message);

  private sealed class Document
  {
    public string Path { get; }
    public byte[] Bytes { get; }
    public string Text { get; }
    public Encoding Encoding { get; }
    public byte[] Preamble { get; }
    public List<Region> Regions { get; }

    public Document(string path, byte[] bytes, string text, Encoding encoding,
      byte[] preamble, List<Region> regions)
    {
      Path = path;
      Bytes = bytes;
      Text = text;
      Encoding = encoding;
      Preamble = preamble;
      Regions = regions;
    }
  }

  private sealed class Region
  {
    public string File { get; }
    public int Line { get; }
    public string Type { get; }
    public string Indent { get; }
    public string Newline { get; }
    public int Start { get; }
    public int Length { get; set; }

    public Region(string file, int line, string type, string indent, string newline, int start)
    {
      File = file;
      Line = line;
      Type = type;
      Indent = indent;
      Newline = newline;
      Start = start;
    }
  }
}
