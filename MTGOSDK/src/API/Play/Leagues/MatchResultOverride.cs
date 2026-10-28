/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.API.Play.Leagues;

/// <summary>
/// An override of a league match's recorded result.
/// </summary>
public enum MatchResultOverride
{
  //#pragma import enum WotC.MTGO.Common.LeagueMatchModificationEnum begin
  NotSet,
  ForceWin,
  ForceLoss,
  Nullify,
  Restore
  //#pragma import enum WotC.MTGO.Common.LeagueMatchModificationEnum end
}
