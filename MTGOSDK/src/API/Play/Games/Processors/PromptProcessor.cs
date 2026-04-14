/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Threading;

using MTGOSDK.API.Play.Games.Processors.EventArgs;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Correlates <see cref="GameStateSnapshot"/> ticks with
/// <see cref="GamePrompt"/> events by matching their shared nonce.
/// </summary>
/// <remarks>
/// <para>
/// The gameplay-status hook fires before <c>set_Prompt</c>, so prompts can
/// arrive either before or after their matching snapshot is processed.
/// Both sides buffer unmatched items in concurrent dictionaries:
/// <see cref="_pendingPrompts"/> for early-arriving prompts and
/// <see cref="_pendingSnapshots"/> for early-arriving snapshots.
/// Whichever side completes the pair emits the correlated event.
/// </para>
/// </remarks>
public sealed class PromptProcessor : IGameStateProcessor
{
  private Game? _game;
  private int _gameId;

  /// <summary>
  /// Prompts buffered by nonce, awaiting their matching snapshot.
  /// Written from the EventHookProxy thread, read from the drain loop.
  /// </summary>
  private readonly ConcurrentDictionary<int, GamePrompt> _pendingPrompts = new();

  /// <summary>
  /// Snapshots buffered by nonce, awaiting their matching prompt.
  /// Written from the drain loop thread, read from the EventHookProxy thread.
  /// </summary>
  private readonly ConcurrentDictionary<int, (GameStateSnapshot Snapshot, GameContext Context)>
    _pendingSnapshots = new();

  /// <summary>
  /// Signaled when all pending snapshots have been matched with their
  /// prompts. Reset when a snapshot is buffered without a prompt match.
  /// Used by <see cref="Game.WaitForPendingProcessing"/> to wait for
  /// the <c>set_Prompt</c> IPC to deliver after the drain loop finishes.
  /// </summary>
  internal readonly ManualResetEventSlim PromptCorrelated = new(true);

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
    _gameId = game.Id; // Cache — avoids IPC on every prompt event
    s_GamePromptChanged += OnGamePromptChanged;

    // Seed the initial prompt (e.g. keep/mulligan) that was already set on
    // the game before we subscribed to set_Prompt. Options are read lazily
    // via IPC — the remote GamePrompt is pinned by the DRO and its
    // m_options is never cleared (new object created per state update).
    var currentPrompt = game.Prompt;
    if (currentPrompt != null)
    {
      _pendingPrompts[currentPrompt.Nonce] = currentPrompt;
    }
  }

  public void Process(GameContext context)
  {
    LastSnapshot = context.Current;
    int nonce = context.Current.Nonce;

    // Prompt arrived before snapshot — correlate now.
    if (_pendingPrompts.TryRemove(nonce, out var prompt))
    {
      LastPrompt = prompt;
      context.Emit(new PromptChangedEventArgs(LastSnapshot, LastPrompt));
    }
    else
    {
      // Snapshot arrived before prompt — buffer for late correlation.
      // Reset the signal so callers can wait for the set_Prompt IPC.
      _pendingSnapshots[nonce] = (LastSnapshot, context);
      PromptCorrelated.Reset();
    }
  }

  private void OnGamePromptChanged(Game game, GamePrompt prompt)
  {
    // Filter to only this game. _gameId is cached from Initialize() to avoid
    // IPC on the stored _game reference, which can become stale.
    if (game.Id != _gameId) return;

    int nonce = prompt.Nonce;

    // Snapshot was already processed — correlate now.
    if (_pendingSnapshots.TryRemove(nonce, out var pending))
    {
      LastPrompt = prompt;
      pending.Context.Emit(
        new PromptChangedEventArgs(pending.Snapshot, LastPrompt));

      // Signal that all pending snapshots have been matched.
      if (_pendingSnapshots.IsEmpty)
        PromptCorrelated.Set();
    }
    else
    {
      // Prompt arrived before snapshot — buffer for correlation in Process().
      _pendingPrompts[nonce] = prompt;
    }
  }

  public void Dispose()
  {
    s_GamePromptChanged -= OnGamePromptChanged;
    _pendingPrompts.Clear();
    _pendingSnapshots.Clear();
  }

  /// <summary>
  /// Ensures the static <c>GamePromptChanged</c> hook is installed
  /// so that prompt detection is active.
  /// </summary>
  public static void EnsureHookInitialized() =>
    s_GamePromptChanged.EnsureInitialize();

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

        dynamic prompt = args[0]; // IGamePrompt
        prompt.__timestamp = instance.__timestamp;
        GamePrompt gamePrompt = new(prompt);

        return (game, gamePrompt);
      })
    );
}
