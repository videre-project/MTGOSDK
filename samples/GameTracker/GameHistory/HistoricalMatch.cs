/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace GameTracker.GameHistory;

public class HistoricalMatch : HistoricalItem
{
  public int MatchId;
  public Guid MatchToken;
  public List<int> GameIds = new();
  public List<string> Opponents = new();
  public int Wins;
  public int Losses;

  public int Ties => GameIds.Count - (Wins + Losses);
  public string Record => $"{Wins}-{Losses}-{Ties}";

  public HistoricalMatch(dynamic Item) : base((object)Item)
  {
    // Update match metadata
    MatchId = Item.EventId;
    // MatchToken = HistoricalMatch.EventToken;

    // Update game metadata
    foreach(int id in Item.GameIds)
    {
      GameIds.Add(id);
    }
    for(int i = 0; i < Item.Opponents.Count; i++)
    {
      Opponents.Add(Item.Opponents[i]);
    }

    // Update match record
    Wins = Item.GameWins;
    Losses = Item.GameLosses;
  }
}
