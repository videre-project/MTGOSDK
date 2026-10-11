/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games;

//WotC.MtGO.Client.Model.Play.GameState
public enum GameStatus
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.GameState begin
  Invalid = 0,
  NotStarted = 1,
  Started = 2,
  Finished = 3,
  //#pragma import enum WotC.MtGO.Client.Model.Play.GameState end
}
