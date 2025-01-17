/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

[Flags]
public enum ActionModifiers
{
  None = 0,
  YieldUntilEvent = 1,
  YieldThroughThisTurn = 2,
  AlwaysYieldToAbility = 4,
  YieldToAbilityUntilEndOfTurn = 8,
  AlwaysAnswerThisWay = 0x10,
  RetainPriority = 0x20
}
