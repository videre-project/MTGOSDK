/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents the current active player and phase in a game.
/// </summary>
public readonly record struct CurrentPlayerPhase(
  GamePlayer ActivePlayer,
  GamePhase CurrentPhase
);
