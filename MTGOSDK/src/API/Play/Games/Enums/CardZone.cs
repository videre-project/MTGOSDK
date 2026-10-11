/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum CardZone
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.CardZone begin
  Invalid = 0,
  Hand = 1,
  Library = 2,
  Graveyard = 3,
  Exile = 4,
  LocalExileCanBePlayed = 5,
  OpponentExileCanBePlayed = 6,
  Battlefield = 7,
  Stack = 8,
  Command = 9,
  Commander = 10,
  Planar = 11,
  Effects = 12,
  LocalTriggers = 13,
  OpponentTriggers = 14,
  Aside = 15,
  Revealed = 16,
  Pile1 = 17,
  Pile2 = 18,
  Pile3 = 19,
  Nowhere = 20,
  Sideboard = 21,
  Mutate = 22,
  Companion = 23,
  //#pragma import enum WotC.MtGO.Client.Model.Play.CardZone end
}
