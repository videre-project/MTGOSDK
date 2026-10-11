/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.API.Play.Tournaments;

public enum TournamentState
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.Tournaments.TournamentState begin
  NotSet = 0,
  Fired = 1,
  WaitingToStart = 2,
  Drafting = 3,
  Deckbuilding = 4,
  DeckbuildingDeckSubmitted = 5,
  WaitingForFirstRoundToStart = 6,
  RoundInProgress = 7,
  BetweenRounds = 8,
  Finished = 9,
  //#pragma import enum WotC.MtGO.Client.Model.Play.Tournaments.TournamentState end
}
