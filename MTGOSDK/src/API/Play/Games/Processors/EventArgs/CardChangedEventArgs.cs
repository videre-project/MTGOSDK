/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event args for card property changes between snapshots.
/// </summary>
public sealed class CardChangedEventArgs(
  GameCard previous,
  GameCard current) : GameEventArgs
{
  public GameCard Previous { get; } = previous;
  public GameCard Current { get; } = current;
}
