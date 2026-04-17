/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event args for player property changes between snapshots.
/// </summary>
public sealed class PlayerChangedEventArgs(
  int playerIndex,
  GamePlayer previous,
  GamePlayer current) : GameEventArgs
{
  public int PlayerIndex { get; } = playerIndex;
  public GamePlayer Previous { get; } = previous;
  public GamePlayer Current { get; } = current;
}
