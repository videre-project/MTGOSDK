/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a card association (e.g. Target, Attacker, Effect source, etc.)
/// </summary>
public struct GameCardAssociation(dynamic gameCardAssociation)
{
  /// <summary>
  /// Represents a card association (e.g. ChosenPlayer, TriggeringSource, etc.).
  /// </summary>
  public CardAssociation Association =
    Cast<CardAssociation>(Unbind(gameCardAssociation).Value);

  /// <summary>
  /// The ID of the associated target.
  /// </summary>
  public int TargetId = gameCardAssociation.AssociatedTarget.Id;
}
