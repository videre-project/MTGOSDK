/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text;


namespace MTGOSDK.API.Chat;

/// <summary>
/// Normalizes MTGO chat/trade text markup into stable plain-text tokens.
/// </summary>
public static class ChatTextNormalizer
{
  private static readonly Dictionary<string, string> s_symbolLookup =
    new(StringComparer.Ordinal)
    {
      ["sW"] = "{W}",
      ["sU"] = "{U}",
      ["sB"] = "{B}",
      ["sR"] = "{R}",
      ["sG"] = "{G}",
      ["s_"] = "{B/G}",
      ["s="] = "{B/R}",
      ["s$"] = "{U/B}",
      ["s`"] = "{U/R}",
      ["s&amp,"] = "{G/U}",
      ["s&"] = "{G/U}",
      ["s-"] = "{G/W}",
      ["s'"] = "{R/G}",
      ["s~"] = "{R/W}",
      ["s,"] = "{W/B}",
      ["s+"] = "{W/U}",
      ["s&gt,"] = "{2/G}",
      ["s>"] = "{2/G}",
      ["s&lt,"] = "{2/R}",
      ["s<"] = "{2/R}",
      ["s%"] = "{2/B}",
      ["s@"] = "{2/U}",
      ["s!"] = "{2/W}",
      ["s0"] = "{0}",
      ["s1"] = "{1}",
      ["s2"] = "{2}",
      ["s3"] = "{3}",
      ["s4"] = "{4}",
      ["s5"] = "{5}",
      ["s6"] = "{6}",
      ["s7"] = "{7}",
      ["s8"] = "{8}",
      ["s9"] = "{9}",
      ["sa"] = "{10}",
      ["sb"] = "{11}",
      ["sc"] = "{12}",
      ["sd"] = "{13}",
      ["se"] = "{14}",
      ["sf"] = "{15}",
      ["sg"] = "{16}",
      ["sh"] = "{17}",
      ["si"] = "{18}",
      ["sj"] = "{19}",
      ["sk"] = "{20}",
      ["sX"] = "{X}",
      ["so"] = "{S}",
      ["sT"] = "{T}",
      ["sJ"] = "{Q}",
      ["sTap"] = "{T}",
      ["sV"] = "[arrow]",
      ["sClone"] = "[clone]",
      ["sCLONE"] = "[clone]",
      ["sD"] = "[trophy]",
      ["sY"] = "[sick]",
      ["sF"] = "[frown]",
      ["sS"] = "[smile]",
      ["sMute"] = "[mute]",
      ["sWiz"] = "[wiz]",
      ["sHat"] = "[wizhat]",
      ["sZ"] = "[zzz]",
      ["sAdept"] = "[adept]",
      ["sClan"] = "[clan]",
      ["sPig"] = "[pig]",
      ["sLizard"] = "[lizard]",
      ["sEventTicket"] = "[event ticket]",
      ["sLifeHeart"] = "[life]",
      ["sCardHand"] = "[hand]"
    };

  private static readonly Dictionary<string, string> s_caseInsensitiveSymbolLookup =
    new(StringComparer.OrdinalIgnoreCase)
    {
      ["sAdept"] = "[adept]",
      ["sCardHand"] = "[hand]",
      ["sClan"] = "[clan]",
      ["sClone"] = "[clone]",
      ["sEventTicket"] = "[event ticket]",
      ["sHat"] = "[wizhat]",
      ["sLifeHeart"] = "[life]",
      ["sLizard"] = "[lizard]",
      ["sMute"] = "[mute]",
      ["sPig"] = "[pig]",
      ["sTap"] = "{T}",
      ["sWiz"] = "[wiz]"
    };

  /// <summary>
  /// Replaces MTGO chat symbols such as <c>[sG]</c> and <c>[sWiz]</c> with
  /// stable text tokens. Unknown bracketed symbols are preserved unchanged.
  /// </summary>
  public static string Normalize(string? rawText)
  {
    if (string.IsNullOrEmpty(rawText))
      return string.Empty;

    var sb = new StringBuilder(rawText.Length);

    for (int index = 0; index < rawText.Length;)
    {
      if (rawText[index] != '[')
      {
        sb.Append(rawText[index++]);
        continue;
      }

      int close = rawText.IndexOf(']', index + 1);
      if (close < 0)
      {
        sb.Append(rawText, index, rawText.Length - index);
        break;
      }

      string token = rawText.Substring(index + 1, close - index - 1);
      if (s_symbolLookup.TryGetValue(token, out string? replacement) ||
          s_caseInsensitiveSymbolLookup.TryGetValue(token, out replacement))
      {
        sb.Append(replacement);
      }
      else
      {
        sb.Append('[');
        sb.Append(token);
        sb.Append(']');
      }

      index = close + 1;
    }

    return sb.ToString();
  }
}
