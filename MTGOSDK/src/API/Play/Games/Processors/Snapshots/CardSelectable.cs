/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games.Processors.Snapshots;

/// <summary>
/// A card view model's detached action and selection flags.
/// </summary>
public sealed record CardSelectable(
  int CardId,
  bool HasActions,
  bool IsSelectable,
  bool IsTargetable,
  bool IsTargeted
)
{
  /// <summary>
  /// Whether MTGO displays the card at normal brightness.
  /// </summary>
  public bool IsActive { get; init; } = true;

  /// <summary>
  /// Whether MTGO marks this card as being cast.
  /// </summary>
  public bool IsBeingCast { get; init; }
}
