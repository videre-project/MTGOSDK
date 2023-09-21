/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace GameTracker.GameHistory;

public class Item
{
  public DateTime StartTime;
  public string Format;
  public string GameType;

  public Item(dynamic HistoricalItem)
  {
    // Update queue/event metadata
    StartTime = HistoricalItem.StartTime;
    GameType = HistoricalItem.DeckCreationStyle.ToString();
    Format = HistoricalItem.GameStructure.Name.Trim();
  }
}
