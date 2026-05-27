/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Tournaments;

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
  /// The current record of the player.
  /// </summary>
  /// <remarks>
  /// The record is formatted as "W-L-D" (i.e. "3-1-0").
  /// </remarks>
  public string Record
  {
    get
    {
      // If we computed a record client-side, return that instead.
      string record = Try(() => Unbind(this).Record, fallback: null);
      if (record != null) return record;

      return ComputeRecord(new
      {
        User = new { Id = Unbind(this).User.Id },
        PreviousMatches = PreviousMatches
      });
    }
  }

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
  // [NonSerializable]
  public IList<MatchStandingRecord> PreviousMatches =>
    Map<IList, MatchStandingRecord>(@base.PreviousMatches);

  //
  // IStandingRecord wrapper methods
  //

  public static string ComputeRecord(dynamic standingRecord)
  {
    int wins = 0;
    int losses = 0;
    int draws = 0;

    int playerId = Unbind(standingRecord.User).Id;
    foreach (var match in standingRecord.PreviousMatches)
    {
      if (!match.State.HasFlag(MatchState.MatchCompleted) && !match.HasBye)
        continue;

      if (match.WinningPlayerIds.Contains(playerId) || match.HasBye)
      {
        wins++;
      }
      else if (match.LosingPlayerIds.Contains(playerId))
      {
        losses++;
      }
      else
      {
        draws++;
      }
    }

    return string.Format("{0}-{1}-{2}", wins, losses, draws);
  }

  public override string ToString()
  {
    return string.Format(
      "{0}. {1} - {2} ({3} points, {4}% OMWP, {5}% GW, {6}% OGWP)",
      Rank, Player, Record, Points, OpponentMatchWinPercentage, GameWinPercentage, OpponentGameWinPercentage
    );
  }
}
