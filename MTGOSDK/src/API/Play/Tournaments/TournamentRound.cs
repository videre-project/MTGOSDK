/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Tournaments;


namespace MTGOSDK.API.Play.Tournaments;

public sealed class TournamentRound(dynamic tournamentRound)
    : DLRWrapper<ITournamentRound>
{
  /// <summary>
  /// Stores an internal reference to the ITournamentRound object.
  /// </summary>
  internal override dynamic obj => tournamentRound;

  //
  // ITournamentRound wrapper properties
  //

  /// <summary>
  /// The tournament round's number.
  /// </summary>
  public int Number => @base.Number;

  /// <summary>
  /// Whether the tournament round is complete.
  /// </summary>
  public bool IsComplete => @base.IsComplete;

  /// <summary>
  /// The tournament round's matches.
  /// </summary>
  public IEnumerable<Match> Matches => Map<Match>(@base.Matches);

  /// <summary>
  /// The start time of the tournament round.
  /// </summary>
  public DateTime StartTime => @base.StartTime;

  /// <summary>
  /// The users assigned a bye in the tournament round.
  /// </summary>
  public IEnumerable<User> UsersWithByes => Map<User>(@base.UsersWithByes);
}
