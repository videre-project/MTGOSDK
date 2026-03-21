/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

/// <summary>
/// Represents the type of a game state element received from MTGO.
/// </summary>
public enum StateElementType
{
  Invalid = 0,
  Card = 1,
  PlayerStatus = 2,
  TurnStep = 3,
  ManaPool = 4,
  Thing = 5,
  Animation = 6,
  DamageAssignment = 7,
  CasualWish = 8,
  CreatureOrdering = 9,
  InteractState = 10,
  CommanderInfoState = 11,
  PlayerAutoYieldInfo = 12,
  DungeonInfoState = 13,
  MiniChange = 200,
}
