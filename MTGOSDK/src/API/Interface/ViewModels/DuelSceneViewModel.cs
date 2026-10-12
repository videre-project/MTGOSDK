/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Reflection;

using Shiny.Core.Enums;
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
  /// <remarks>
  /// Requires the <c>Shiny.Core.Enums</c> reference assembly.
  /// </remarks>
  public GameViewType LayoutType =>
    Cast<GameViewType>(Unbind(this).LayoutType);

  /// <summary>
  /// Whether the DuelScene is a replay of a previous game.
  /// </summary>
  public bool IsReplay => Unbind(this).IsReplay;

  //
  // ReplayCommand wrapper methods
  //

  /// <summary>
  /// Starts the playback of a replay.
  /// </summary>
  public void StartReplay() =>
    Unbind(this).PlayOrPauseReplayCommand.Execute(true);

  /// <summary>
  /// Pauses the playback of a replay.
  /// </summary>
  public void PauseReplay() =>
    Unbind(this).PlayOrPauseReplayCommand.Execute(false);

  /// <summary>
  /// Halts the playback of a replay and closes the DuelScene.
  /// </summary>
  public void CloseReplay() =>
    Unbind(this).CloseReplayCommand.Execute();

  /// <summary>
  /// Forwards the playback of a replay to the next game action.
  /// </summary>
  public void ReplayNextAction() =>
    Unbind(this).PlayOrPauseReplayCommand.Execute(null);

  /// <summary>
  /// Forwards the playback of a replay to the next game step.
  /// </summary>
  public void ReplayNextStep() =>
    Unbind(this).NextStepReplayCommand.Execute();

  /// <summary>
  /// Forwards the playback of a replay to the next turn.
  /// </summary>
  public void ReplayNextTurn() =>
    Unbind(this).NextTurnReplayCommand.Execute();

  //
  // Live UI positions
  //

  /// <summary>
  /// Gets a card's transformed screen bounds, or null when its view cannot be captured.
  /// </summary>
  public CardViewModel.ScreenBounds? GetCardPosition(int cardId) =>
    GetCardPositions([cardId]).GetValueOrDefault(cardId)?.Bounds;

  /// <summary>
  /// Reads screen bounds and drawing order for cards with visible views.
  /// </summary>
  /// <remarks>
  /// The current views and their transformed corners are captured in one client
  /// UI-thread batch. Bounds include rotation, scaling, and ancestor transforms.
  /// Failed rows and invisible views are omitted. Drawing order can be compared
  /// only for cards with the same panel index in this result. A view can move
  /// after capture, and covering visuals can obscure its bounds.
  /// </remarks>
  public IReadOnlyDictionary<int, CardViewModel.Position> GetCardPositions(
    IEnumerable<int> cardIds) => CardViewModel.GetPositions(this, cardIds);

  /// <summary>
  /// Resolves the live duel scene retained by MTGO's match window.
  /// </summary>
  /// <remarks>
  /// Uses the native match lookup, including league and tournament ownership.
  /// Returns null until the window has a scene for this game. A delayed spectator
  /// scene can exist before it replaces the window's current scene.
  /// </remarks>
  public static DuelSceneViewModel? ForGame(Game game)
  {
    ArgumentNullException.ThrowIfNull(game);

    dynamic? owner = Unbind(s_playScene).GetViewModelForJoinable(Unbind(game).Match);
    if (owner is null)
      return null;

    int gameId = game.Id;
    dynamic? scene = owner.m_duelSceneViewModel;
    if (scene is not null && scene.GameId == gameId && !scene.IsDisposed)
    {
      return new DuelSceneViewModel(scene);
    }

    scene = owner.m_delayedDuelSceneViewModel;
    if (scene is not null && scene.GameId == gameId && !scene.IsDisposed)
    {
      return new DuelSceneViewModel(scene);
    }

    return null;
  }

  private static readonly IPlaySceneViewModel s_playScene =
    ObjectProvider.Get<IPlaySceneViewModel>();
}
