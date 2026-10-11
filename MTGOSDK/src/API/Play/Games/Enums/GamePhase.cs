/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum GamePhase
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.GamePhase begin
  Invalid = 0,
  Untap = 1,
  Upkeep = 2,
  Draw = 3,
  PreCombatMain = 4,
  BeginCombat = 5,
  DeclareAttackers = 6,
  DeclareBlockers = 7,
  CombatDamage = 8,
  EndOfCombat = 9,
  PostCombatMain = 10,
  EndOfTurn = 11,
  Cleanup = 12,
  //#pragma import enum WotC.MtGO.Client.Model.Play.GamePhase end

  // Client pregame values absent from the native enum declaration.
  PreGame1 = 13,
  PreGame2 = 14,
}
