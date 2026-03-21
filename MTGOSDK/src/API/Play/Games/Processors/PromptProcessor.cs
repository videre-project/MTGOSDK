/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Correlates <see cref="GameStateSnapshot"/> ticks with
/// <see cref="GamePrompt"/> events by matching their shared nonce.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="GameProcessor"/> typically fires before
/// the game's prompt-changed event, so this processor buffers
/// the latest snapshot and emits a <see cref="PromptChangedEventArgs"/>
/// through the centralized bus once the matching prompt arrives.
/// </para>
/// </remarks>
public sealed class PromptProcessor : IGameStateProcessor
{
  private Game? _game;
  private GameContext? _lastContext;

  /// <summary>
  /// The most recent snapshot received from the processor pipeline.
  /// </summary>
  public GameStateSnapshot? LastSnapshot { get; private set; }

  /// <summary>
  /// The most recent prompt received from the game event.
  /// </summary>
  public GamePrompt? LastPrompt { get; private set; }

  public void Initialize(Game game)
  {
    _game = game;
    s_GamePromptChanged += OnGamePromptChanged;
  }

  public void Process(GameContext context)
  {
    _lastContext = context;
    LastSnapshot = context.Current;

    // If we already have a prompt waiting, try to correlate immediately.
    if (LastPrompt != null)
      TryCorrelate(context);
  }

  private void OnGamePromptChanged(Game game, GamePrompt prompt)
  {
    // Filter to only this game
    if (game.Id != _game.Id) return;
    LastPrompt = prompt;

    // If we already have a snapshot waiting, try to correlate immediately.
    if (LastSnapshot != null && _lastContext != null)
      TryCorrelate(_lastContext);
  }

  private void TryCorrelate(GameContext context)
  {
    if (LastSnapshot == null || LastPrompt == null) return;

    if (LastSnapshot.Nonce == LastPrompt.Nonce)
    {
      context.Emit(new PromptChangedEventArgs(LastSnapshot, LastPrompt));
    }
  }

  public void Dispose()
  {
    s_GamePromptChanged -= OnGamePromptChanged;
  }

  /// <summary>
  /// Event triggered when the current game prompt changes in any active game.
  /// </summary>
  private static EventHookProxy<Game, GamePrompt> s_GamePromptChanged =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game>(),
      "set_Prompt",
      new((instance, args) =>
      {
        Game game = new(instance);
        DateTime __timestamp = instance.__timestamp;

        dynamic prompt = args[0]; // IGamePrompt
        GamePrompt gamePrompt = new(new
        {
          // DynamicRemoteObject properties
          __timestamp,
          // IGamePrompt properties
          Text = prompt.Text,
          Timestamp = prompt.Timestamp,
          PromptedPlayer = prompt.PromptedPlayer,
          Options = Map<GameAction>(prompt.Options),
        });

        return (game, gamePrompt);
      })
    );
}
