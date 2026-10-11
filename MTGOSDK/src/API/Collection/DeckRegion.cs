/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Collection;

[Flags]
public enum DeckRegion
{
  //#pragma import enum WotC.MTGO.Common.DeckRegionEnum begin
  NotSet = 0,
  MainDeck = 1,
  Sideboard = 2,
  CommandZone = 4,
  Planechase = 8,
  Vanguard = 16,
  Hidden = 32,
  //#pragma import enum WotC.MTGO.Common.DeckRegionEnum end
}
