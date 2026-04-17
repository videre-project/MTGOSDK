/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event arguments emitted when cards enter or leave a revealed or pile zone.
/// Each card's <see cref="GameCard.Zone"/> identifies the specific zone
/// (<c>Revealed</c>, <c>Pile1</c>, <c>Pile2</c>, or <c>Pile3</c>).
/// </summary>
public sealed class RevealedCardsEventArgs : GameEventArgs
{
  /// <summary>Cards newly visible in a revealed or pile zone this tick.</summary>
  public required List<GameCard> Arrived  { get; init; }

  /// <summary>Cards that left a revealed or pile zone this tick.</summary>
  public required List<GameCard> Departed { get; init; }

  /// <summary>All cards currently in a revealed or pile zone.</summary>
  public required List<GameCard> Current  { get; init; }
}
