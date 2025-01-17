/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents a game action performed by the client (without the game server).
/// </summary>
public sealed class LocalAction(dynamic localAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the LocalAction object.
  /// </summary>
  internal override dynamic obj => Bind<IGameAction>(localAction);
}
