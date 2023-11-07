/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Manager for the player's game replays.
/// </summary>
public static class ReplayManager
{
  //
  // GameReplayService wrapper methods
  //

  /// <summary>
  /// Global replay service for requesting and dispatching game replays.
  /// </summary>
  private static readonly IGameReplayService s_gameReplayService =
    ObjectProvider.Get<IGameReplayService>();

  /// <summary>
  /// Whether game replays are allowed by the server.
  /// </summary>
  public static bool ReplaysAllowed =>
    s_gameReplayService.AreReplaysAllowed;

  /// <summary>
  /// Sends a request to the PlayerEventManager to start a replay.
  /// </summary>
  /// <param name="gameId">The game ID to replay.</param>
  /// <returns>True if the request was sent successfully.</returns>
  public static bool RequestReplay(int gameId) =>
    s_gameReplayService.RequestReplay(gameId);

  /// <summary>
  /// The currently active replay, if any.
  /// </summary>
  public static Replay ActiveReplay =>
    new(DLRWrapper<dynamic>.Unbind(s_gameReplayService).m_replayEvent);

  //
  // PlayerEventManager wrapper methods
  //

  /// <summary>
  /// Global manager for all player events (e.g. tournaments) and replays.
  /// </summary>
  private static readonly IPlayerEventManager s_playerEventManager =
    ObjectProvider.Get<IPlayerEventManager>();

  /// <summary>
  /// Checks if a replay is currently active for a given game.
  /// </summary>
  /// <param name="gameId">The game ID to check.</param>
  /// <returns>True if a replay for the game is active.</returns>
  public static bool IsReplayActive(int gameId) =>
    s_playerEventManager.IsReplayInProgress(gameId);
}