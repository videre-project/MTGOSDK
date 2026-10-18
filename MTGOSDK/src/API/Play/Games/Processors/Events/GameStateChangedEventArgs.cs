/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.Snapshots;


namespace MTGOSDK.API.Play.Games.Processors.Events;

/// <summary>
/// A correlated gameplay snapshot and its captured client-local decision state.
/// The event nonce identifies the combined state. Snapshot.Nonce retains the
/// originating server prompt identity.
/// </summary>
public sealed class GameStateChangedEventArgs(
  GamePrompt prompt,
  InteractionStateSnapshot interaction,
  PromptOptionsSnapshot options) : GameEventArgs
{
  /// <summary>
  /// The correlated native prompt with captured client controls.
  /// </summary>
  public GamePrompt Prompt { get; } = prompt;

  /// <summary>
  /// The detached interaction values associated with this prompt,
  /// including native actions and selection flags in <c>Cards</c>.
  /// </summary>
  public InteractionStateSnapshot Interaction { get; } = interaction;

  /// <summary>
  /// Captured prompt options and their control execution revision.
  /// </summary>
  public PromptOptionsSnapshot Options { get; } = options;
}
