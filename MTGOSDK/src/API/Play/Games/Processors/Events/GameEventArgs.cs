/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games.Processors.Snapshots;


namespace MTGOSDK.API.Play.Games.Processors.Events;

/// <summary>
/// Base class for all game processor event arguments. Carries the game state
/// update nonce and context from the snapshot that was active when the event
/// was emitted.
/// </summary>
public abstract class GameEventArgs
{
  /// <summary>
  /// The identity of this update, assigned by the centralized event bus.
  /// Correlation events use the snapshot's prompt nonce. Combined state updates
  /// also include captured local interaction values; their originating server
  /// nonce remains available through <see cref="Snapshot"/>.
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
