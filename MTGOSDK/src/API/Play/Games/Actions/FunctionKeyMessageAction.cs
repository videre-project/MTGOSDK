/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public class FunctionKeyMessageAction(dynamic functionKeyMessageAction)
    : GameAction
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj => Bind<IGameAction>(functionKeyMessageAction);

  //
  // FunctionKeyMessageAction wrapper properties
  //

  public FunctionKey Key =>
    Cast<FunctionKey>(Unbind(this).MessageParameter);
}
