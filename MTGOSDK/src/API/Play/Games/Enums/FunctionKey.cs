/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

public enum FunctionKey : byte
{
  Invalid = 0,
  DisableAutoYields = 3,
  YieldThroughThisTurn = 6,
  FastStacking = 7,
  DisableBluffing = 8
}
