/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum CardZone
{
  Invalid,
  Hand,
  Library,
  Graveyard,
  Exile,
  LocalExileCanBePlayed,
  OpponentExileCanBePlayed,
  Battlefield,
  Stack,
  Command,
  Commander,
  Planar,
  Effects,
  LocalTriggers,
  OpponentTriggers,
  Aside,
  Revealed,
  Pile1,
  Pile2,
  Pile3,
  Nowhere,
  Sideboard,
  Mutate,
  Companion
}
