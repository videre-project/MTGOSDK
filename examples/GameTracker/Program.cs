/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOInjector;
using GameTracker.GameHistory;


var client = new MTGOClient();
dynamic SettingsService = client.ObjectProvider("SettingsService");

dynamic manager = SettingsService.GameHistoryManager;
if (!manager.HistoryLoaded)
  throw new Exception("Game history not loaded.");

//
// Enumerate the client's game history.
//
// HistoricalItems can be either a HistoricalMatch or HistoricalTournament type.
//
int i = 0;
foreach(dynamic Item in manager.Items)
{
  Console.WriteLine($"{i}: {Item.GetType()}");
  i++;

  switch(Item.GetType().Name)
  {
    case "HistoricalItem":
    case "HistoricalMatch":
      Match match = new(Item);
      Console.WriteLine($"Event #:       {match.MatchId}");
      Console.WriteLine($"Format:        {match.Format}");
      Console.WriteLine($"Date and Time: {match.StartTime}");
      // PlayStyle
      // Name
      Console.WriteLine($"GameType:      {match.GameType}");
      Console.WriteLine($"Record:        {match.Record}");
      break;
    case "HistoricalTournament":
      // Tournament tournament = new(Item);
      break;
  }
}
