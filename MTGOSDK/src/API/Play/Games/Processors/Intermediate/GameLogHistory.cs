/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.API.Play.Games.Processors.Intermediate;

/// <summary>
/// Locates game openings in the ordered history of a shared match log.
/// </summary>
internal static class GameLogHistory
{
  internal readonly record struct Entry(DateTime Timestamp, string Text);

  internal readonly record struct Range(int Start, int End);

  /// <summary>
  /// Separates games at an opening following an explicit game result.
  /// Repeated results and joins during an unfinished game retain their segment.
  /// </summary>
  internal static List<Range> Split(IReadOnlyList<Entry> messages,
    IReadOnlyCollection<string> players)
  {
    var ranges = new List<Range>();
    int start = -1;
    bool ended = false;
    for (int index = 0; index < messages.Count; index++)
    {
      string text = messages[index].Text;
      while (text.StartsWith("@P", StringComparison.Ordinal)) text = text[2..];

      bool opening = players.Any(player =>
        text == $"{player} joined the game." ||
        text.StartsWith($"{player} rolled a ", StringComparison.Ordinal));
      bool result = text == "The game is a draw." || players.Any(player =>
        text == $"{player} wins the game." ||
        text == $"{player} loses the game." || text == $"{player} wins!");

      if (opening && (start < 0 || ended))
      {
        if (start >= 0) ranges.Add(new(start, index));
        start = index;
        ended = false;
      }
      if (start >= 0 && result) ended = true;
    }
    if (start >= 0) ranges.Add(new(start, messages.Count));
    return ranges;
  }
}
