/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace GameTracker.GameHistory;

public class HistoricalItem
{
  public DateTime StartTime;
  public string Format;
  public string GameType;

  public HistoricalItem(dynamic Item)
  {
    // Update queue/event metadata
    StartTime = Item.StartTime;
    GameType = Item.DeckCreationStyle.ToString();
    Format = Item.GameStructure.Name.Trim();
  }
}
