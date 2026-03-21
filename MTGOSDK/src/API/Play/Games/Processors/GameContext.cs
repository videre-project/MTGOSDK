/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Play.Games.Processors.EventArgs;


namespace MTGOSDK.API.Play.Games.Processors;


/// <summary>
/// Paired current/previous snapshots passed to <see cref="IGameStateProcessor"/>
/// instances after a new game state is built from a gameplay-status message.
/// </summary>
public sealed class GameContext
{
  /// <summary>
  /// The current game state snapshot.
  /// </summary>
  public required GameStateSnapshot Current { get; init; }

  /// <summary>
  /// The previous game state snapshot.
  /// </summary>
  public required GameStateSnapshot Previous { get; init; }

  /// <summary>
  /// Maps current ThingIDs to their previous ThingIDs for cards that changed ID.
  /// </summary>
  public Dictionary<int, int> CardAncestryMap { get; } = new();

  /// <summary>
  /// Reference to the host processor for centralized event dispatch.
  /// </summary>
  internal GameProcessor? Processor { get; init; }

  /// <summary>
  /// Emits an event through the centralized event bus on the host
  /// <see cref="GameProcessor"/>. The <see cref="GameEventArgs.Context"/>
  /// and <see cref="GameEventArgs.Nonce"/> are stamped automatically.
  /// </summary>
  public void Emit<T>(T args) where T : GameEventArgs =>
    Processor?.Dispatch(args, this);
}
