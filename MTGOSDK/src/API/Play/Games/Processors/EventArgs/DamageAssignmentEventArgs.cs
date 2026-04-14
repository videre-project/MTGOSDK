/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event args for combat damage assignments extracted from server state elements.
/// Contains one entry per attacking creature that assigned damage.
/// </summary>
public sealed class DamageAssignmentEventArgs(
    List<CombatDamageAssignmentAction> assignments) : GameEventArgs
{
  public List<CombatDamageAssignmentAction> Assignments { get; } = assignments;
}
