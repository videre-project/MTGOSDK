/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Events.Tournaments;

public sealed class StandingRecord(dynamic standingRecord)
    : DLRWrapper<IStandingRecord>
{
  /// <summary>
  /// Stores an internal reference to the IStandingRecord object.
  /// </summary>
  internal override dynamic obj => standingRecord;

  //
  // IStandingRecord wrapper properties
  //

  /// <summary>
  /// The rank of the user in the tournament.
  /// </summary>
  public int Rank => @base.Rank;

  /// <summary>
  /// The user object of the player.
  /// </summary>
  public User Player => new(@base.User);

  /// <summary>
  /// The number of points the player has earned.
  /// </summary>
  public int Points => @base.Points;

  /// <summary>
  /// The average match win percentage of the player's opponents.
  /// </summary>
  public string OpponentMatchWinPercentage => @base.OpponentMatchWinPercentage;

  /// <summary>
  /// The average game win percentage of the player.
  /// </summary>
  public string GameWinPercentage => @base.GameWinPercentage;

  /// <summary>
  /// The average game win percentage of the player's opponents.
  /// </summary>
  public string OpponentGameWinPercentage => @base.OpponentGameWinPercentage;

  /// <summary>
  /// The match history of the player.
  /// </summary>
  public IEnumerable<MatchStandingRecord> PreviousMatches
  {
    get
    {
      foreach (var matchRecord in @base.PreviousMatches)
        yield return new MatchStandingRecord(matchRecord);
    }
  }
}
