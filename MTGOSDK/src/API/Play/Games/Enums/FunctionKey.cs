/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum FunctionKey : byte
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.Actions.FunctionKeyMessageAction+FunctionKey begin
  Invalid = 0,
  DisableAutoYields = 3,
  YieldThroughThisTurn = 6,
  FastStacking = 7,
  DisableBluffing = 8,
  YieldThroughOpponentActionsControlledByPlayer = 9,
  //#pragma import enum WotC.MtGO.Client.Model.Play.Actions.FunctionKeyMessageAction+FunctionKey end
}
