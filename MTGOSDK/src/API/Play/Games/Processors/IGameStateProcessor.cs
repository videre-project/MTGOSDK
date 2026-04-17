/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games.Processors;

/// <summary>
/// Modular processor component that processes each game snapshot.
/// Implementations may maintain their own internal state across snapshots.
/// Called from the single drain task — not under the queue lock.
/// </summary>
public interface IGameStateProcessor
{
  /// <summary>
  /// Initializes the processor for a specific game instance.
  /// Allows the processor to subscribe to events on the game object.
  /// </summary>
  void Initialize(Game game) { }

  /// <summary>
  /// Processes a new snapshot against the previous state.
  /// Called from the single drain task — not under the queue lock.
  /// </summary>
  void Process(GameContext context);
}
