/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Base class for all game processor event arguments. Carries the game state
/// update nonce and context from the snapshot that was active when the event
/// was emitted.
/// </summary>
public abstract class GameEventArgs
{
  /// <summary>
  /// The game state update nonce for this event, derived from the snapshot's
  /// interaction timestamp, prompted player, and prompt text.
  /// Set automatically when the event is dispatched through the centralized bus.
  /// </summary>
  public int Nonce { get; internal set; }

  /// <summary>
  /// The game context that was active when this event was emitted,
  /// giving access to the current and previous snapshots.
  /// </summary>
  public GameContext Context { get; internal set; } = null!;

  /// <summary>
  /// Shorthand for <c>Context.Current</c>.
  /// </summary>
  public GameStateSnapshot Snapshot => Context.Current;
}
