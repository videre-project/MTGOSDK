/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Collections.ObjectModel;
using System.Text;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents the game prompt in a running game.
/// </summary>
public sealed class GamePrompt(dynamic gamePrompt) : DLRWrapper<IGamePrompt>
{
  private readonly IReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>>?
    _options;

  internal GamePrompt(
    dynamic gamePrompt,
    IReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>> options)
      : this((object)gamePrompt)
  {
    _options = options;
  }

  /// <summary>
  /// Strips MTGO prompt markup (e.g. <c>@[Card Name@:123]</c> → <c>Card Name</c>)
  /// from raw prompt text. Mirrors the logic in the MTGO client's
  /// <c>GamePrompt.RemoveMarkupFromText</c>.
  /// </summary>
  /// <remarks>
  /// Both game state snapshots and <see cref="GamePrompt"/> must use
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
  /// this prompt with the corresponding game state snapshot.
  /// </summary>
  public int Nonce => ComputeNonce(Timestamp, PromptedPlayer, Text);

  /// <summary>
  /// Computes a correlation hash from prompt state fields.
  /// </summary>
  internal static int ComputeNonce(uint timestamp, byte promptedPlayer, string promptText)
  {
    var hash = new HashCode();
    hash.Add(timestamp);
    hash.Add(promptedPlayer);
    hash.Add(promptText ?? string.Empty, StringComparer.Ordinal);
    return hash.ToHashCode();
  }

  /// <summary>
  /// The available game actions for the prompt.
  /// </summary>
  /// <remarks>
  /// Reads the native <c>IDictionary&lt;ActionType, IList&lt;IGameAction&gt;&gt;</c>
  /// and wraps each action using its native action type.
  /// </remarks>
  public IDictionary<ActionType, IList<GameAction>> GameActions
  {
    get
    {
      if (_options != null)
        return _options.Where(group => group.Key != ActionType.PromptControl)
          .ToDictionary(group => group.Key, group => (IList<GameAction>)
            Array.AsReadOnly(group.Value.OfType<GameAction>().ToArray()));
      // Use the raw captured DRO directly as Unbind(@base) can return a
      // CachingRemoteProxy (from batch hydration) that doesn't support
      // iterating complex collections like dictionaries.
      dynamic rawOptions = Unbind(gamePrompt).Options;

      Func<dynamic, ActionType> keyFunc = k => Cast<ActionType>(k);
      Func<dynamic, IList<GameAction>> valueFunc =
        v => Map<IList, GameAction>(v, GameAction.GameActionFactory);

      var options = new Dictionary<ActionType, IList<GameAction>>();
      foreach (var kvp in Map<IDictionary, ActionType, IList<GameAction>>(
        rawOptions, keyFunc, valueFunc))
      {
        options[kvp.Key] = kvp.Value;
      }

      return options;
    }
  }

  /// <summary>
  /// Prompt options exposed by the game client, grouped by action type.
  /// </summary>
  public IReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>> Options
    => _options
      ?? new ReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>>(
        GameActions.ToDictionary(
          group => group.Key,
          group => (IReadOnlyList<IPromptOption>)Array.AsReadOnly(
            group.Value.Cast<IPromptOption>().ToArray())));
}
