/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace GameTracker.GameHistory;

public class Tournament : Item
{
  public int EventId;
  public List<Match> Matches = new();
  public int Wins;
  public int Losses;

  public int Ties => Matches.Count - (Wins + Losses);
  public string Record => $"{Wins}-{Losses}-{Ties}";

  public Tournament(dynamic HistoricalTournament) : base((object)HistoricalTournament)
  {
    // Update tournament metadata
    EventId = HistoricalTournament.EventId;

    // Add match entries
    foreach(int HistoricalMatch in HistoricalTournament.GameIds)
    {
      Matches.Add(new Match(HistoricalMatch));
    }

    // Update tournament record
    Wins = HistoricalTournament.MatchWins;
    Losses = HistoricalTournament.MatchLosses;
  }
}
