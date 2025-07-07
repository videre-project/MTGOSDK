/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection.Serialization;


namespace MTGOSDK.API.Play;

public class PlayerResult : SerializableBase
{
  /// <summary>
  /// The player who won or lost the match.
  /// </summary>
  public string Player { get; set; }

  /// <summary>
  /// The result of the match for the player (Win, Loss, Draw).
  /// </summary>
  public MatchResult Result { get; set; }

  public int Wins { get; set; } = 0;
  public int Losses { get; set; } = 0;
  public int Draws { get; set; } = 0;

  public PlayerResult()
  {
    Player = string.Empty;
    Result = MatchResult.NotSet;
  }

  public PlayerResult(string player, MatchResult result, int wins, int losses, int draws)
  {
    Player = player;
    Result = result;
    Wins = wins;
    Losses = losses;
    Draws = draws;
  }

  public PlayerResult(User player, IList<GameResult> results)
  {
    Player = player.Name;
    foreach (GameResult result in results)
    {
      switch (result)
      {
        case GameResult.Win:
          Wins++;
          break;
        case GameResult.Loss:
          Losses++;
          break;
        default:
          Draws++;
          break;
      }

      // Determine the match result based on the number of wins/losses/draws.
      if (Wins > Losses)
      {
        Result = MatchResult.Win;
      }
      else if (Losses > Wins)
      {
        Result = MatchResult.Loss;
      }
      else
      {
        Result = MatchResult.Draw;
      }
    }
  }
}
