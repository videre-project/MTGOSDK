/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Reflection;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface.ViewModels;

public sealed class DuelSceneViewModel(dynamic duelSceneViewModel)
    : DLRWrapper<IDuelSceneViewModel>
{
  /// <summary>
  /// Stores an internal reference to the IDuelSceneViewModel object.
  /// </summary>
  internal override dynamic obj => Bind<IDuelSceneViewModel>(duelSceneViewModel);

  //
  // IDuelSceneViewModel wrapper properties
  //

  /// <summary>
  /// The game id of the DuelScene game.
  /// </summary>
  public int GameId => @base.GameId;

  /// <summary>
  /// The game object of the DuelScene game.
  /// </summary>
  public Game Game => new(@base.Game);

  /// <summary>
  /// The layout type of the DuelScene (e.g. Solitare, Duel, Multiplayer).
  /// </summary>
  public GameViewType LayoutType =>
    Cast<GameViewType>(Unbind(@base).LayoutType);

  /// <summary>
  /// Whether the DuelScene is a replay of a previous game.
  /// </summary>
  public bool IsReplay => Unbind(@base).IsReplay;

  //
  // ReplayCommand wrapper methods
  //

  /// <summary>
  /// Starts the playback of a replay.
  /// </summary>
  public void StartReplay() =>
    Unbind(@base).PlayOrPauseReplayCommand.Execute(true);

  /// <summary>
  /// Pauses the playback of a replay.
  /// </summary>
  public void PauseReplay() =>
    Unbind(@base).PlayOrPauseReplayCommand.Execute(false);

  /// <summary>
  /// Halts the playback of a replay and closes the DuelScene.
  /// </summary>
  public void CloseReplay() =>
    Unbind(@base).CloseReplayCommand.Execute();

  /// <summary>
  /// Forwards the playback of a replay to the next game action.
  /// </summary>
  public void ReplayNextAction() =>
    Unbind(@base).PlayOrPauseReplayCommand.Execute(null);

  /// <summary>
  /// Forwards the playback of a replay to the next game step.
  /// </summary>
  public void ReplayNextStep() =>
    Unbind(@base).NextStepReplayCommand.Execute();

  /// <summary>
  /// Forwards the playback of a replay to the next turn.
  /// </summary>
  public void ReplayNextTurn() =>
    Unbind(@base).NextTurnReplayCommand.Execute();
}
