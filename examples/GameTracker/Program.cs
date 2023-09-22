/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOInjector;
using GameTracker.GameHistory;


var client = new MTGOClient();

dynamic SettingsService = client.ObjectProvider("SettingsService");
dynamic manager = SettingsService.GameHistoryManager;
if (!manager.HistoryLoaded)
  throw new Exception("Game history not loaded.");

// dynamic GameHistoryVM = client.ObjectProvider("GameHistoryViewModel");
dynamic gameReplayService = client.ObjectProvider("GameReplayService");

//
// Enumerate the client's game history.
//
// HistoricalItems can be either a HistoricalMatch or HistoricalTournament type.
//
int i = 0;
bool isReplayActive = false;
foreach(dynamic Item in manager.Items)
{
  Console.WriteLine($"{i}: {Item.GetType()}");
  i++;

  switch(Item.GetType().Name)
  {
    case "HistoricalItem":
    case "HistoricalMatch":
      Match match = new(Item);
      Console.WriteLine($"Match Id:      {match.MatchId}");
      Console.WriteLine($"Format:        {match.Format}");
      Console.WriteLine($"Date and Time: {match.StartTime}");
      // PlayStyle
      Console.WriteLine($"Name:          {match.Opponents.FirstOrDefault()}");
      Console.WriteLine($"GameType:      {match.GameType}");
      Console.WriteLine($"Record:        {match.Record}");

      // TODO: Populate match class based on game history data
      //dynamic gameHistoryData = GameHistoryVM.PopulateMatchHistoryData(Item)
      //  ?? throw new Exception("Failed to populate game history data.");

      //
      // Request a replay for the first game in the match.
      //
      if (!isReplayActive)
      {
        int gameId = match.GameIds.First();
        isReplayActive = gameReplayService.RequestReplay(gameId);
        if (!isReplayActive)
          throw new Exception($"Failed to request replay for Game #{gameId}.");

        Console.WriteLine($"\nRequested replay for Game #{gameId}.");
      }

      break;
    case "HistoricalTournament":
      // Tournament tournament = new(Item);

      // dynamic tournamentHistoryData = GameHistoryVM.PopulateTournamentHistoryData(Item)
      //   ?? throw new Exception("Failed to populate tournament history data.");

      break;
  }
}
