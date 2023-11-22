/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;

/// <summary>
/// Represents the game prompt in a running game.
/// </summary>
public sealed class GamePrompt(dynamic gamePrompt) : DLRWrapper<IGamePrompt>
{
  /// <summary>
  /// Stores an internal reference to the IGamePrompt object.
  /// </summary>
  internal override dynamic obj => Bind<IGamePrompt>(gamePrompt);

  //
  // IGamePrompt wrapper properties
  //

  /// <summary>
  /// The current text of the prompt.
  /// </summary>
  public string Text => @base.Text;

  /// <summary>
  /// Whether the prompt is a mulligan prompt.
  /// </summary>
  public bool IsMulligan => @base.IsMulligan;

  /// <summary>
  /// The available game actions for the prompt.
  /// </summary>
  public IEnumerable<GameAction> Options =>
    Map<GameAction>(@base.Options.Values);
}
