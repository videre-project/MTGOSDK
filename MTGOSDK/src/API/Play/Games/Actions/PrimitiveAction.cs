/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class PrimitiveAction(dynamic primitiveAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the PrimitiveAction object.
  /// </summary>
  internal override dynamic obj => Bind<IGameAction>(primitiveAction);
}
