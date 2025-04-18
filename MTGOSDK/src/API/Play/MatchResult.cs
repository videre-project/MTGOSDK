/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;


namespace MTGOSDK.API.Play;

public enum MatchResult
{
  NotSet = -1,
  Win = GameResult.Win,
  Loss = GameResult.Loss,
  Draw
}
