/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Text;

using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents the game prompt in a running game.
/// </summary>
public sealed class GamePrompt(dynamic gamePrompt) : DLRWrapper<IGamePrompt>
{
  /// <summary>
  /// Strips MTGO prompt markup (e.g. <c>@[Card Name@:123]</c> → <c>Card Name</c>)
  /// from raw prompt text. Mirrors the logic in the MTGO client's
  /// <c>GamePrompt.RemoveMarkupFromText</c>.
  /// </summary>
  /// <remarks>
  /// Both <see cref="GameStateSnapshot"/> and <see cref="GamePrompt"/> must use
  /// the same cleaned text when computing the nonce, otherwise interact-state
  /// prompts with card-name markup will fail to correlate.
  /// </remarks>
  public static string RemoveMarkup(string text)
  {
    if (text == null) return string.Empty;
    if (!text.Contains('@')) return text; // Fast path — no markup

    var sb = new StringBuilder(text.Length);
    var chars = text.AsSpan();
    int len = chars.Length;

    for (int i = 0; i < len; i++)
    {
      if (chars[i] == '@' && i + 1 < len)
      {
        char next = chars[++i];
        if (next == ':')
        {
          // @:...] — skip metadata until closing bracket
          while (i < len && chars[i] != ']') i++;
        }
        else if (next == '[')
        {
          // @[text content — extract until next '@' marker
          while (++i < len)
          {
            if (chars[i] == '@') { i--; break; }
            sb.Append(chars[i]);
          }
        }
        else
        {
          // @X — keep both characters
          sb.Append('@');
          sb.Append(next);
        }
      }
      else
      {
        sb.Append(chars[i]);
      }
    }

    return sb.ToString();
  }

  /// <summary>
  /// Stores an internal reference to the IGamePrompt object.
  /// </summary>
  internal override dynamic obj => Bind<IGamePrompt>(gamePrompt);

  //
  // IGamePrompt wrapper properties
  //

  /// <summary>
  /// The current text of the prompt.
  /// </summary>
  public string Text =>
    field ??= MTGOSDK.API.MtgoTextNormalizer.NormalizeText(RemoveMarkup(@base.Text));

  /// <summary>
  /// The current interaction timestamp of the game.
  /// </summary>
  [NonSerializable]
  public uint Timestamp => Unbind(this).Timestamp;

  /// <summary>
  /// The player index that this prompt targets (byte.MaxValue = all players).
  /// </summary>
  public byte PromptedPlayer => @base.PromptedPlayer;

  /// <summary>
  /// A deterministic nonce derived from the prompt state, used to correlate
  /// this prompt with the corresponding <see cref="GameStateSnapshot"/>.
  /// </summary>
  public int Nonce => GameStateSnapshot.ComputeNonce(Timestamp, PromptedPlayer, Text);

  /// <summary>
  /// The available game actions for the prompt.
  /// </summary>
  /// <remarks>
  /// Handles both the remote <c>IDictionary&lt;ActionType, IList&lt;IGameAction&gt;&gt;</c>
  /// and the materialized <c>IEnumerable&lt;GameAction&gt;</c> produced by
  /// <see cref="PromptProcessor"/> via <c>Map&lt;GameAction&gt;</c>.
  /// </remarks>
  public IDictionary<ActionType, IList<GameAction>> Options
  {
    get
    {
      var options = new Dictionary<ActionType, IList<GameAction>>();
      // Use the raw captured DRO directly — Unbind(@base) can return a
      // CachingRemoteProxy (from batch hydration) that doesn't support
      // iterating complex collections like dictionaries.
      dynamic rawOptions = Unbind(gamePrompt).Options;
      Func<dynamic, ActionType> keyFunc = k => Cast<ActionType>(k);
      Func<dynamic, IList<GameAction>> valueFunc =
        v => Map<IList, GameAction>(v, GameAction.GameActionFactory);
      foreach (var kvp in Map<IDictionary, ActionType, IList<GameAction>>(
        rawOptions, keyFunc, valueFunc))
      {
        options[kvp.Key] = kvp.Value;
      }
      return options;
    }
  }
}
