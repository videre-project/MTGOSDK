/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.Events;

/// <summary>
/// Event arguments emitted when captured reveals arrive or depart.
/// Includes native revealed-zone cards and temporarily exposed library or pile cards.
/// Each card retains its captured identity and zone.
/// </summary>
public sealed class RevealedCardsEventArgs : GameEventArgs
{
  /// <summary>
  /// Cards newly exposed in this snapshot.
  /// </summary>
  public required List<GameCard> Arrived  { get; init; }

  /// <summary>
  /// Cards that departed, retaining their last captured values.
  /// </summary>
  public required List<GameCard> Departed { get; init; }

  /// <summary>
  /// All currently captured reveals.
  /// </summary>
  public required List<GameCard> Current  { get; init; }
}
