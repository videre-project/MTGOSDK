/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection.Serialization;


namespace MTGOSDK.API.Play.Games;

public class PlayerResult(
  GamePlayer player,
  PlayDrawResult playDraw,
  GameResult result,
  TimeSpan clock) : IJsonSerializable
{
  /// <summary>
  /// The player who won or lost the game.
  /// </summary>
  public string Player { get; init; } = player.Name;

  /// <summary>
  /// The direction of play for the player (Play or Draw).
  /// </summary>
  public PlayDrawResult PlayDraw { get; init; } = playDraw;

  /// <summary>
  /// The result of the game for the player (Win, Loss, Draw).
  /// </summary>
  public GameResult Result { get; init; } = result;

  /// <summary>
  /// The time remaining on the player's clock at the end of the game.
  /// </summary>
  public TimeSpan Clock { get; init; } = clock;
}
