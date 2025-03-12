/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

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
  /// The current interaction timestamp of the game.
  /// </summary>
  [NonSerializable]
  public uint Timestamp => Unbind(@base).Timestamp;

  /// <summary>
  /// The available game actions for the prompt.
  /// </summary>
  public IList<GameAction>? Options =>
    Optional(Map<IList, GameAction>(@base.Options.Values),
            Lambda<bool>((_) => @base.Options.Count > 0));
}
