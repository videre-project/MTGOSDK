/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace GameTracker.GameHistory;

public class Match : Item
{
  public int MatchId;
  public Guid MatchToken;
  public List<int> GameIds = new();
  public List<string> Opponents = new();
  public int Wins;
  public int Losses;

  public int Ties => GameIds.Count - (Wins + Losses);
  public string Record => $"{Wins}-{Losses}-{Ties}";

  public Match(dynamic HistoricalMatch) : base((object)HistoricalMatch)
  {
    // Update match metadata
    MatchId = HistoricalMatch.EventId;
    // MatchToken = HistoricalMatch.EventToken;

    // Update game metadata
    foreach(int id in HistoricalMatch.GameIds)
    {
      GameIds.Add(id);
    }
    for(int i = 0; i < HistoricalMatch.Opponents.Count; i++)
    {
      Opponents.Add(HistoricalMatch.Opponents[i]);
    }

    // Update match record
    Wins = HistoricalMatch.GameWins;
    Losses = HistoricalMatch.GameLosses;
  }
}
