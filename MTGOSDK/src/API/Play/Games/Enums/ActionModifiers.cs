/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

[Flags]
public enum ActionModifiers
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.ActionModifiers begin
  None = 0,
  YieldUntilEvent = 1,
  YieldThroughThisTurn = 2,
  AlwaysYieldToAbility = 4,
  YieldToAbilityUntilEndOfTurn = 8,
  AlwaysAnswerThisWay = 16,
  RetainPriority = 32,
  YieldThroughOpponentActionsControlledByPlayer = 64,
  //#pragma import enum WotC.MtGO.Client.Model.Play.ActionModifiers end
}
