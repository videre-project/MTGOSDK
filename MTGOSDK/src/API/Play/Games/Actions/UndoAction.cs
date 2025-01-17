/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a game action that undoes the previous action.
/// </summary>
public sealed class UndoAction(dynamic localAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the UndoAction object.
  /// </summary>
  internal override dynamic obj => Bind<IGameAction>(localAction);
}
