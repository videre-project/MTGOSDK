/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API;
using MTGOSDK.API.Play.History;


var client = new Client();

if (!HistoryManager.HistoryLoaded)
  throw new Exception("Game history not loaded.");

foreach(var item in HistoryManager.Items)
{
  var type = item.GetType().Name;
  Console.WriteLine(type);
  switch (type)
  {
    case "HistoricalItem":
    case "HistoricalMatch":
      Console.WriteLine($"  Id: {item.Id}");
      Console.WriteLine($"  Game 1 Id: {item.GameIds[0]}");
      break;
    case "HistoricalTournament":
      Console.WriteLine($"  Id: {item.Id}");
      Console.WriteLine($"  Match 1 Id: {item.Matches.First().Id}");
      break;
  }
}
