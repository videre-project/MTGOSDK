/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event args for a correlated snapshot/prompt pair.
/// </summary>
public sealed class PromptChangedEventArgs(
  GameStateSnapshot snapshot,
  GamePrompt prompt) : GameEventArgs
{
  /// <summary>
  /// The game state snapshot that produced this correlation.
  /// </summary>
  public new GameStateSnapshot Snapshot { get; } = snapshot;

  /// <summary>
  /// The game prompt that matched the snapshot's nonce.
  /// </summary>
  public GamePrompt Prompt { get; } = prompt;
}
