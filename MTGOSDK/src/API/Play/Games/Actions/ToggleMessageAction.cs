/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public class ToggleMessageAction(dynamic toggleMessageAction)
    // We override the base instance with the IGameCard interface.
    : FunctionKeyMessageAction(null)
{
  /// <summary>
  /// Stores an internal reference to the IGameCard object.
  /// </summary>
  internal override dynamic obj => Bind<IGameAction>(toggleMessageAction);

  //
  // ToggleMessageAction wrapper properties
  //

  public bool ToggleState => Unbind(this).ToggleState;
}
