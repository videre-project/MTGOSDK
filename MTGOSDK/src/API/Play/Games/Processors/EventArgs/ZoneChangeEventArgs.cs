/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event arguments for zone change events.
/// </summary>
public sealed class ZoneChangeEventArgs : GameEventArgs
{
  public required List<GameCard> Arrived { get; init; }
  public required List<GameCard> Departed { get; init; }
  public required List<(GameCard From, GameCard To)> Moved { get; init; }
  public required List<string> ChainLogs { get; init; }
  public required List<string> UnresolvedOrigins { get; init; }
}
