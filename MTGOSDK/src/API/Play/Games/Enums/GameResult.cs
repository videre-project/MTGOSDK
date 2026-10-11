/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

// Subset of WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game.FlsGameResult.
public enum GameResult
{
  // StillPlaying = 1,
  Win = 2,
  Loss = 3,
}
