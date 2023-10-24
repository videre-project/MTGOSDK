/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Events.Leagues;

public sealed class LeagueLeaderboardEntry(dynamic leagueLeaderboardEntry)
    : DLRWrapper<ILeagueLeaderboardEntry>
{
  /// <summary>
  /// Stores an internal reference to the ILeagueLeaderboardEntry object.
  /// </summary>
  internal override dynamic obj => leagueLeaderboardEntry;

  //
  // ILeagueLeaderboardEntry wrapper properties
  //

  /// <summary>
  /// The last date the player earned a trophy in the league.
  /// </summary>
  public DateTime LastTrophyEarnedDate => @base.LastTrophyEarnedDate;

  /// <summary>
  /// The player's username.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The number of trophies the player has earned in the league.
  /// </summary>
  public int TrophyCount => @base.TrophyCount;
}
