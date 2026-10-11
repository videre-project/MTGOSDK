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
  //#pragma import enum WotC.MtGO.Client.Model.Play.StateElementType begin
  Invalid = 0,
  Card = 1,
  PlayerStatus = 2,
  TurnStep = 3,
  ManaPool = 4,
  Thing = 5,
  Animation = 6,
  DamageAssignment = 7,
  CreatureOrdering = 8,
  InteractState = 9,
  CommanderInfoState = 10,
  PlayerAutoYieldInfo = 11,
  DungeonInfoState = 12,
  MiniChange = 200,
  //#pragma import enum WotC.MtGO.Client.Model.Play.StateElementType end
}
