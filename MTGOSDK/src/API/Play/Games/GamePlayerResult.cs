/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json.Serialization;

using MTGOSDK.Core.Reflection.Serialization;


namespace MTGOSDK.API.Play.Games;

[method: JsonConstructor]
public class GamePlayerResult(
  string player,
  PlayDrawResult playDraw,
  GameResult result,
  TimeSpan clock) : SerializableBase
{
  /// <summary>
  /// The player who won or lost the game.
  /// </summary>
  public string Player { get; set; } = player;

  /// <summary>
  /// The direction of play for the player (Play or Draw).
  /// </summary>
  public PlayDrawResult PlayDraw { get; set; } = playDraw;

  /// <summary>
  /// The result of the game for the player (Win, Loss, Draw).
  /// </summary>
  public GameResult Result { get; set; } = result;

  /// <summary>
  /// The time remaining on the player's clock at the end of the game.
  /// </summary>
  public TimeSpan Clock { get; set; } = clock;

  public GamePlayerResult(
    GamePlayer player,
    PlayDrawResult playDraw,
    GameResult result,
    TimeSpan clock)
      : this(player.Name, playDraw, result, clock)
  { } // constructor for serialization
}
