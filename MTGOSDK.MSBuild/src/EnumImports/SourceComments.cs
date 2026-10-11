/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

namespace MTGOSDK.MSBuild;

// The region format is textual, independent of namespace or enum declaration
// syntax. Scan actual standalone comments so examples inside literals are safe.
internal static class SourceComments
{
  internal sealed class Comment
  {
    public int Start { get; }
    public int Length { get; }
    public int Line { get; }
    public string Newline { get; }
    public Comment(int start, int length, int line, string newline)
    { Start = start; Length = length; Line = line; Newline = newline; }
  }

  internal static IEnumerable<Comment> Standalone(string text)
  {
    int position = 0, lineStart = 0, line = 1;
    while (position < text.Length)
    {
      var start = position;
      if (At(text, position, "//"))
      {
        var end = text.IndexOf('\n', position);
        if (end < 0) end = text.Length;
        var contentEnd = end > position && text[end - 1] == '\r' ? end - 1 : end;
        bool standalone = true;
        for (var i = lineStart; i < position; i++)
          if (text[i] != ' ' && text[i] != '\t') { standalone = false; break; }
        if (standalone)
          yield return new Comment(lineStart, contentEnd - lineStart, line,
            end == text.Length ? "" : text.Substring(contentEnd, end - contentEnd + 1));
        position = end;
      }
      else if (At(text, position, "/*")) position = SkipBlock(text, position);
      else if (!SkipLiteral(text, ref position)) position++;

      for (var i = start; i < position; i++)
        if (text[i] == '\n') { line++; lineStart = i + 1; }
    }
  }

  private static int SkipBlock(string text, int position)
  {
    var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
    return end < 0 ? text.Length : end + 2;
  }

  private static bool SkipLiteral(string text, ref int position)
  {
    var quote = position;
    bool verbatim = false, interpolated = false;
    if (quote < text.Length && text[quote] == '@') { verbatim = true; quote++; }
    while (quote < text.Length && text[quote] == '$') { interpolated = true; quote++; }
    if (quote < text.Length && text[quote] == '@') { verbatim = true; quote++; }
    if (quote >= text.Length || (text[quote] != '"'
      && (quote != position || text[quote] != '\''))) return false;

    var delimiter = text[quote];
    int count = 1;
    if (delimiter == '"' && !verbatim)
      while (quote + count < text.Length && text[quote + count] == '"') count++;
    if (count >= 3)
    {
      var end = text.IndexOf(new string('"', count), quote + count, StringComparison.Ordinal);
      position = end < 0 ? text.Length : end + count;
      return true;
    }

    position = quote + 1;
    while (position < text.Length)
    {
      if (!verbatim && text[position] == '\\')
      { position = Math.Min(position + 2, text.Length); continue; }
      if (text[position] == delimiter)
      {
        position++;
        if (verbatim && position < text.Length && text[position] == delimiter)
        { position++; continue; }
        return true;
      }
      if (interpolated && text[position] == '{')
      {
        if (At(text, position, "{{")) position += 2;
        else SkipInterpolation(text, ref position);
      }
      else position++;
    }
    return true;
  }

  private static void SkipInterpolation(string text, ref int position)
  {
    int depth = 1;
    position++;
    while (position < text.Length && depth > 0)
    {
      if (At(text, position, "//"))
      {
        var end = text.IndexOf('\n', position);
        position = end < 0 ? text.Length : end;
      }
      else if (At(text, position, "/*")) position = SkipBlock(text, position);
      else if (SkipLiteral(text, ref position)) { }
      else
      {
        if (text[position] == '{') depth++;
        if (text[position] == '}') depth--;
        position++;
      }
    }
  }

  private static bool At(string text, int position, string value) =>
    position + value.Length <= text.Length
      && string.CompareOrdinal(text, position, value, 0, value.Length) == 0;
}
