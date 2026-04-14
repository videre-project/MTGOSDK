/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text;


namespace MTGOSDK.API;

/// <summary>
/// Converts MTGO mana encodings into Scryfall-style symbol syntax while
/// preserving MTGO text markup such as <c>@i</c> and <c>@-</c>.
/// </summary>
public static class MtgoTextNormalizer
{
  private static readonly string[] s_namedSymbols = ["CHAOS", "TK", "PW"];
  private static readonly Dictionary<char, string> s_encodedGenericValues = new()
  {
    ['a'] = "10",
    ['b'] = "11",
    ['c'] = "12",
    ['d'] = "13",
    ['e'] = "14",
    ['f'] = "15",
    ['g'] = "16",
    ['h'] = "17",
    ['i'] = "18",
    ['j'] = "19",
    ['k'] = "20",
  };
  private static readonly HashSet<char> s_directSymbols =
    ['W', 'U', 'B', 'R', 'G', 'C', 'X', 'Y', 'Z', 'T', 'Q', 'E', 'P', 'A', 'L', 'D', 'H'];
  private static readonly HashSet<char> s_slashSegmentSymbols =
    ['W', 'U', 'B', 'R', 'G', 'C', 'P', 'H'];

  /// <summary>
  /// Converts an MTGO mana cost string such as <c>1#gw-#gw-</c> into
  /// Scryfall-style symbols such as <c>{1}{G/W}{G/W}</c>.
  /// </summary>
  public static string NormalizeManaCost(string? rawManaCost)
  {
    if (string.IsNullOrEmpty(rawManaCost))
      return string.Empty;

    return TryNormalizeSymbolStream(rawManaCost, out string normalized)
      ? normalized
      : rawManaCost;
  }

  /// <summary>
  /// Normalizes MTGO brace payloads in mixed-content text while leaving
  /// non-symbol markup such as <c>@i</c> unchanged.
  /// </summary>
  public static string NormalizeText(string? rawText)
  {
    if (string.IsNullOrEmpty(rawText))
      return string.Empty;

    var sb = new StringBuilder(rawText.Length + 8);

    for (int index = 0; index < rawText.Length;)
    {
      int open = rawText.IndexOf('{', index);
      if (open < 0)
      {
        sb.Append(rawText, index, rawText.Length - index);
        break;
      }

      sb.Append(rawText, index, open - index);

      int close = rawText.IndexOf('}', open + 1);
      if (close < 0)
      {
        sb.Append(rawText, open, rawText.Length - open);
        break;
      }

      string payload = rawText.Substring(open + 1, close - open - 1);
      if (TryNormalizeSymbolStream(payload, out string normalized))
      {
        sb.Append(normalized);
      }
      else
      {
        sb.Append('{');
        sb.Append(payload);
        sb.Append('}');
      }

      index = close + 1;
    }

    return sb.ToString();
  }

  private static bool TryNormalizeSymbolStream(
    string value,
    out string normalized)
  {
    var sb = new StringBuilder(value.Length * 3);

    for (int index = 0; index < value.Length;)
    {
      // Order matters here:
      // 1. Compact #.... tokens must win before any simpler character-based parser.
      // 2. Signed values like +1/-1 should stay intact instead of being split.
      // 3. Slash forms like u/p should normalize as a single symbol.
      // 4. Pure numbers should normalize before the fallback single-char parser.
      // 5. Named tokens like TK/CHAOS should stay grouped.
      // 6. The final fallback handles concatenated runs of simple MTGO symbols.
      if (TryAppendCompactToken(value, ref index, sb) ||
          TryAppendSignedToken(value, ref index, sb) ||
          TryAppendSlashToken(value, ref index, sb) ||
          TryAppendNumberToken(value, ref index, sb) ||
          TryAppendNamedToken(value, ref index, sb) ||
          TryAppendSimpleToken(value, ref index, sb))
      {
        continue;
      }

      normalized = value;
      return false;
    }

    normalized = sb.ToString();
    return true;
  }

  // Parses MTGO's compact 4-character encodings, which are the main
  // representation used in MANA_COST_STRING and also appear in some oracle
  // reminder text. These are not already Scryfall-shaped; they are MTGO's
  // internal shorthand for hybrid, twobrid, phyrexian, and hybrid-phyrexian
  // symbols. Examples:
  // #gw- -> {G/W}
  // #2w- -> {2/W}
  // #wp- -> {W/P}
  // #wup -> {W/U/P}
  // #p-- -> {C/P}
  private static bool TryAppendCompactToken(
    string value,
    ref int index,
    StringBuilder sb)
  {
    if (value[index] != '#' || index + 3 >= value.Length)
      return false;

    string compact = value.Substring(index, 4).ToLowerInvariant();
    if (!TryNormalizeCompactToken(compact, out string? symbol))
      return false;

    AppendSymbol(sb, symbol);
    index += 4;
    return true;
  }

  // Maps the four-character compact encoding into the canonical Scryfall
  // payload string, without braces. The suffix determines the symbol family:
  // - trailing '-' means a two-part compact symbol such as hybrid, twobrid,
  //   colorless hybrid, or single-color phyrexian
  // - trailing 'p' means a two-color phyrexian hybrid symbol
  // Anything outside those known MTGO forms is rejected so the caller can
  // leave the original payload untouched.
  private static bool TryNormalizeCompactToken(
    string compact,
    out string? symbol)
  {
    if (compact == "#p--")
    {
      symbol = "C/P";
      return true;
    }

    char first = compact[1];
    char second = compact[2];
    char third = compact[3];

    if (third == '-')
      return TryNormalizeCompactHyphenatedToken(first, second, out symbol);

    if (third == 'p' && IsColor(first) && IsColor(second))
    {
      symbol =
        $"{char.ToUpperInvariant(first)}/{char.ToUpperInvariant(second)}/P";
      return true;
    }

    symbol = null;
    return false;
  }

  // Handles the MTGO compact forms whose final character is '-' rather than 'p'.
  // These cover four distinct symbol families:
  // - #xy-  -> two-color hybrid, e.g. #gw- -> {G/W}
  // - #2x-  -> twobrid, e.g. #2w- -> {2/W}
  // - #cx-  -> colorless hybrid, e.g. #cw- -> {C/W}
  // - #xp-  -> single-color phyrexian, e.g. #wp- -> {W/P}
  // Grouping them here keeps the compact grammar in one place so a future
  // compact family can be added without touching the outer tokenizer.
  private static bool TryNormalizeCompactHyphenatedToken(
    char first,
    char second,
    out string? symbol)
  {
    if (first == '2' && IsColor(second))
    {
      symbol = $"2/{char.ToUpperInvariant(second)}";
      return true;
    }

    if (first == 'c' && IsColor(second))
    {
      symbol = $"C/{char.ToUpperInvariant(second)}";
      return true;
    }

    if (IsColor(first) && second == 'p')
    {
      symbol = $"{char.ToUpperInvariant(first)}/P";
      return true;
    }

    if (IsColor(first) && IsColor(second))
    {
      symbol = $"{char.ToUpperInvariant(first)}/{char.ToUpperInvariant(second)}";
      return true;
    }

    symbol = null;
    return false;
  }

  // Parses signed numeric payloads such as {+1}, {-1}, or {-12}. These are not
  // mana symbols, but they do use the same brace-delimited symbol rendering in
  // MTGO card text, so we preserve them as a single normalized token instead of
  // letting later parsers split them into punctuation plus digits.
  private static bool TryAppendSignedToken(
    string value,
    ref int index,
    StringBuilder sb)
  {
    char sign = value[index];
    if ((sign != '+' && sign != '-') ||
        index + 1 >= value.Length ||
        !IsDigit(value[index + 1]))
    {
      return false;
    }

    int start = index++;
    while (index < value.Length && IsDigit(value[index]))
      index++;

    AppendSymbol(sb, value.Substring(start, index - start));
    return true;
  }

  // Parses slash-delimited payloads that already look close to Scryfall
  // syntax but may need case canonicalization. This is mainly for oracle-text
  // payloads like {u/p}, and it also accepts already-canonical forms such as
  // {W/U/P}. The goal here is to normalize the existing slash expression as
  // one symbol, not to infer new structure.
  private static bool TryAppendSlashToken(
    string value,
    ref int index,
    StringBuilder sb)
  {
    int end = index;
    bool sawSlash = false;

    while (end < value.Length && IsSlashSymbolChar(value[end]))
    {
      sawSlash |= value[end] == '/';
      end++;
    }

    if (!sawSlash)
      return false;

    string candidate = value.Substring(index, end - index);
    if (!TryCanonicalizeSlashToken(candidate, out string? symbol))
      return false;

    AppendSymbol(sb, symbol);
    index = end;
    return true;
  }

  // Validates and canonicalizes a slash-delimited payload as a two-part or
  // three-part symbol. This intentionally mirrors the Scryfall-style forms we
  // want to emit: W/U, 2/W, C/P, W/U/P, etc. Anything with too many segments
  // or with non-symbol segments is rejected so we do not over-interpret an
  // arbitrary brace token as a mana symbol.
  private static bool TryCanonicalizeSlashToken(
    string candidate,
    out string? symbol)
  {
    string[] parts = candidate.Split('/');
    if (parts.Length is < 2 or > 3)
    {
      symbol = null;
      return false;
    }

    for (int i = 0; i < parts.Length; i++)
    {
      if (!TryCanonicalizeSlashSegment(parts[i], out string? part))
      {
        symbol = null;
        return false;
      }
      parts[i] = part;
    }

    symbol = string.Join("/", parts);
    return true;
  }

  // Validates one segment inside a slash-delimited symbol. A segment must be a
  // single symbol atom that can legally participate in forms like W/U, C/P, or
  // 2/W. Today that means:
  // - one numeric character such as 2
  // - one letter designator such as W, U, B, R, G, C, P, or H
  // Multi-character words are rejected here on purpose.
  private static bool TryCanonicalizeSlashSegment(
    string segment,
    out string? canonical)
  {
    if (string.IsNullOrEmpty(segment))
    {
      canonical = null;
      return false;
    }

    if (segment.Length == 1)
    {
      char value = segment[0];
      if (IsDigit(value))
      {
        canonical = segment;
        return true;
      }

      char upper = char.ToUpperInvariant(value);
      if (s_slashSegmentSymbols.Contains(upper))
      {
        canonical = upper.ToString();
        return true;
      }
    }

    canonical = null;
    return false;
  }

  // Parses generic numeric symbols like {0}, {2}, {15}, or the leading numeric
  // portion of a compact mana-cost stream such as 2R or 10GG. This branch
  // consumes the full run of digits so later parsers do not split multi-digit
  // values into separate symbols.
  private static bool TryAppendNumberToken(
    string value,
    ref int index,
    StringBuilder sb)
  {
    if (!IsDigit(value[index]))
      return false;

    int start = index++;
    while (index < value.Length && IsDigit(value[index]))
      index++;

    AppendSymbol(sb, value.Substring(start, index - start));
    return true;
  }

  // Parses multi-character named symbols that MTGO emits as a single payload.
  // These are not safely handled by the single-character fallback because they
  // would otherwise be split apart. Current examples include:
  // - TK    -> ticket counter
  // - CHAOS -> planar die chaos symbol
  // - PW    -> planeswalker symbol
  private static bool TryAppendNamedToken(
    string value,
    ref int index,
    StringBuilder sb)
  {
    foreach (string token in s_namedSymbols)
    {
      if (!StartsWithOrdinalIgnoreCase(value, index, token))
        continue;

      AppendSymbol(sb, token);
      index += token.Length;
      return true;
    }

    return false;
  }

  // Final fallback for the simple MTGO symbol alphabet. This branch handles:
  // - one-character symbols such as W, U, B, R, G, C, X, T, Q, E, P, A, H
  // - lowercase p, which MTGO uses for snow and which we normalize to {S}
  // - lowercase a-k, which MTGO uses as compact generic 10-20 values
  // - concatenated runs such as WUBRG, CC, EE, or 4GG after the numeric part
  //   has already been consumed by earlier parsers
  // This is intentionally last because it is the most permissive branch.
  private static bool TryAppendSimpleToken(
    string value,
    ref int index,
    StringBuilder sb)
  {
    if (TryNormalizeSimpleToken(value[index], out string? symbol))
    {
      AppendSymbol(sb, symbol);
      index++;
      return true;
    }

    return false;
  }

  // Central lookup for one-character MTGO symbol atoms. This is the main place
  // to extend if MTGO adds a new direct symbol letter or a new encoded generic
  // alias. Keeping the mapping here avoids scattering hard-coded alphabets
  // across the parser.
  private static bool TryNormalizeSimpleToken(char value, out string? symbol)
  {
    if (s_encodedGenericValues.TryGetValue(value, out symbol))
      return true;

    if (value == 'p')
    {
      symbol = "S";
      return true;
    }

    char upper = char.ToUpperInvariant(value);
    if (s_directSymbols.Contains(upper))
    {
      symbol = upper.ToString();
      return true;
    }

    symbol = null;
    return false;
  }

  private static void AppendSymbol(StringBuilder sb, string symbol)
  {
    sb.Append('{');
    sb.Append(symbol);
    sb.Append('}');
  }

  private static bool StartsWithOrdinalIgnoreCase(
    string value,
    int index,
    string token)
  {
    if (index + token.Length > value.Length)
      return false;

    for (int i = 0; i < token.Length; i++)
    {
      if (char.ToUpperInvariant(value[index + i]) != token[i])
        return false;
    }

    return true;
  }

  private static bool IsColor(char value) =>
    "wubrg".IndexOf(char.ToLowerInvariant(value)) >= 0;

  private static bool IsDigit(char value) => value is >= '0' and <= '9';

  private static bool IsSlashSymbolChar(char value) =>
    char.IsLetterOrDigit(value) || value == '/';
}
