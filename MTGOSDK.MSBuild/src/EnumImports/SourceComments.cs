/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

namespace MTGOSDK.MSBuild;

/// <summary>
/// Finds standalone source comments while skipping block comments and literals
/// that can contain examples of enum import markers.
/// </summary>
internal static class SourceComments
{
  internal sealed class Comment
  {
    public int Start { get; }
    public int Length { get; }
    public int Line { get; }
    public string Newline { get; }

    public Comment(int start, int length, int line, string newline)
    {
      Start = start;
      Length = length;
      Line = line;
      Newline = newline;
    }
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
        if (end < 0)
          end = text.Length;

        var contentEnd = end > position && text[end - 1] == '\r' ? end - 1 : end;
        bool standalone = true;
        for (var i = lineStart; i < position; i++)
        {
          if (text[i] != ' ' && text[i] != '\t')
          {
            standalone = false;
            break;
          }
        }

        if (standalone)
          yield return new Comment(lineStart, contentEnd - lineStart, line,
            end == text.Length ? "" : text.Substring(contentEnd, end - contentEnd + 1));
        position = end;
      }
      else if (At(text, position, "/*"))
      {
        position = SkipBlock(text, position);
      }
      else if (!SkipLiteral(text, ref position))
      {
        position++;
      }

      for (var i = start; i < position; i++)
      {
        if (text[i] == '\n')
        {
          line++;
          lineStart = i + 1;
        }
      }
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
    bool verbatim = false;
    int dollarCount = 0;
    if (quote < text.Length && text[quote] == '@')
    {
      verbatim = true;
      quote++;
    }
    while (quote < text.Length && text[quote] == '$')
    {
      dollarCount++;
      quote++;
    }
    if (quote < text.Length && text[quote] == '@')
    {
      verbatim = true;
      quote++;
    }
    if (quote >= text.Length || (text[quote] != '"'
      && (quote != position || text[quote] != '\'')))
      return false;

    var delimiter = text[quote];
    int quoteCount = 1;
    if (delimiter == '"' && !verbatim)
    {
      while (quote + quoteCount < text.Length && text[quote + quoteCount] == '"')
        quoteCount++;
    }
    if (quoteCount >= 3)
    {
      position = quote + quoteCount;
      SkipRawLiteral(text, ref position, quoteCount, dollarCount);
      return true;
    }

    position = quote + 1;
    while (position < text.Length)
    {
      if (!verbatim && text[position] == '\\')
      {
        position = Math.Min(position + 2, text.Length);
        continue;
      }
      if (text[position] == delimiter)
      {
        position++;
        if (verbatim && position < text.Length && text[position] == delimiter)
        {
          position++;
          continue;
        }
        return true;
      }
      if (dollarCount != 0 && text[position] == '{')
      {
        if (At(text, position, "{{"))
          position += 2;
        else
          SkipInterpolation(text, ref position);
      }
      else
      {
        position++;
      }
    }
    return true;
  }

  private static void SkipRawLiteral(
    string text, ref int position, int quoteCount, int dollarCount)
  {
    var delimiter = new string('"', quoteCount);
    while (position < text.Length)
    {
      if (At(text, position, delimiter))
      {
        position += quoteCount;
        return;
      }
      if (dollarCount != 0 && text[position] == '{')
      {
        int braceCount = 1;
        while (position + braceCount < text.Length && text[position + braceCount] == '{')
          braceCount++;

        position += braceCount;
        if (braceCount >= dollarCount)
        {
          // Any preceding braces belong to the literal. The final group opens
          // an expression, whose nested strings have their own delimiters.
          position -= dollarCount;
          SkipInterpolation(text, ref position, dollarCount);
        }
      }
      else
      {
        position++;
      }
    }
  }

  private static void SkipInterpolation(string text, ref int position, int braceCount = 1)
  {
    int depth = 1;
    var delimiter = new string('}', braceCount);
    position += braceCount;
    while (position < text.Length)
    {
      if (At(text, position, "//"))
      {
        var end = text.IndexOf('\n', position);
        position = end < 0 ? text.Length : end;
        continue;
      }
      if (At(text, position, "/*"))
      {
        position = SkipBlock(text, position);
        continue;
      }
      if (SkipLiteral(text, ref position))
        continue;

      if (depth == 1 && At(text, position, delimiter))
      {
        position += braceCount;
        return;
      }
      if (text[position] == '{')
        depth++;
      if (text[position] == '}')
        depth--;
      position++;
    }
  }

  private static bool At(string text, int position, string value) =>
    position + value.Length <= text.Length
      && string.CompareOrdinal(text, position, value, 0, value.Length) == 0;
}
