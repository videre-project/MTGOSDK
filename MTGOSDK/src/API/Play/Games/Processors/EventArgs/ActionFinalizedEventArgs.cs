/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play.Games.Processors.EventArgs;

/// <summary>
/// Event args for a finalized and reconciled game action.
/// </summary>
public sealed class ActionFinalizedEventArgs(GameAction action) : GameEventArgs
{
  public GameAction Action { get; } = action;
}
