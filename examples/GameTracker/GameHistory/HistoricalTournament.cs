/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace GameTracker.GameHistory;

public class HistoricalTournament : HistoricalItem
{
  public int EventId;
  public List<HistoricalMatch> Matches = new();
  public int Wins;
  public int Losses;

  public int Ties => Matches.Count - (Wins + Losses);
  public string Record => $"{Wins}-{Losses}-{Ties}";

  public HistoricalTournament(dynamic Item) : base((object)Item)
  {
    // Update tournament metadata
    EventId = Item.EventId;

    // Add match entries
    foreach(int item in Item.Matches)
    {
      Matches.Add(new HistoricalMatch(item));
    }

    // Update tournament record
    Wins = Item.MatchWins;
    Losses = Item.MatchLosses;
  }
}
