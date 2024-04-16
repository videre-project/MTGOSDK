/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.History;


//
// We implicitly instantiate the RemoteClient through accessing HistoryManager.
//
// Because we do not explicitly create a MTGOSDK.API.Client instance, we are
// expecting to connect to an already running instance of the MTGO client
// without any special permissions or configuration.
//
if (!HistoryManager.HistoryLoaded)
  throw new OperationCanceledException("History not loaded.");

//
// Here we iterate through items collected by the MTGO client's HistoryManager,
// which include all games, matches, and tournaments played by the current user.
//
// This is broken down roughly by event type, which is wrapped into the two
// HistoricalMatch and HistoricalTournament classes; the HistoricalItem class
// is the base class for all items in the history.
//
foreach(var item in HistoryManager.Items)
{
  var type = item.GetType().Name;
  Console.WriteLine(type);
  switch (type)
  {
    case "HistoricalItem":
    case "HistoricalMatch":
      Console.WriteLine($"  Match Id: {item.Id}");
      // For brevity, we'll select only the first game id.
      Console.WriteLine($"  Game 1 Id: {item.GameIds[0]}");
      break;
    case "HistoricalTournament":
      Console.WriteLine($"  Tournament Id: {item.Id}");
      // For brevity, we'll select only the first match id.
      Console.WriteLine($"  Match 1 Id: {item.Matches.First().Id}");
      break;
  }
}
