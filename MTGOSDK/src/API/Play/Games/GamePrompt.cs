/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Play.Games.Processors;
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
  public uint Timestamp => Unbind(this).Timestamp;

  /// <summary>
  /// The player index that this prompt targets (byte.MaxValue = all players).
  /// </summary>
  public byte PromptedPlayer => @base.PromptedPlayer;

  /// <summary>
  /// A deterministic nonce derived from the prompt state, used to correlate
  /// this prompt with the corresponding <see cref="GameStateSnapshot"/>.
  /// </summary>
  public int Nonce =>
    GameStateSnapshot.ComputeNonce(Timestamp, PromptedPlayer, Text);

  /// <summary>
  /// The available game actions for the prompt.
  /// </summary>
  public IDictionary<ActionType, IList<GameAction>> Options
  {
    get
    {
      var options = new Dictionary<ActionType, IList<GameAction>>();
      foreach (var kvp in @base.Options)
      {
        var actionType = Cast<ActionType>(kvp.Key);
        var actionList = new List<GameAction>();
        foreach (var action in kvp.Value)
        {
          actionList.Add(GameAction.GameActionFactory(action));
        }
        options[actionType] = actionList;
      }
      return options;
    }
  }
}
