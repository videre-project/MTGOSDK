/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.API.Play.Tournaments;

public enum TournamentState
{
  NotSet,
  Fired,
  WaitingToStart,
  Drafting,
  Deckbuilding,
  DeckbuildingDeckSubmitted,
  WaitingForFirstRoundToStart,
  RoundInProgress,
  BetweenRounds,
  Finished,
}
