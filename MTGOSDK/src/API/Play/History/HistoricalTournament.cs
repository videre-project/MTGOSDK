/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Play.Tournaments;

using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Represents a historical tournament.
/// </summary>
public sealed class HistoricalTournament(dynamic historicalTournament)
    : HistoricalItem<Tournament>
{
  /// <summary>
  /// Stores an internal reference to the IHistoricalTournament object.
  /// </summary>
  internal override dynamic obj =>
    Bind<IHistoricalTournament>(historicalTournament);

  //
  // IHistoricalTournament wrapper properties
  //

  /// <summary>
  /// The player's matches in the tournament.
  /// </summary>
  public IList<HistoricalMatch> Matches =>
    field ??= Map<IList, HistoricalMatch>(Unbind(@base).Matches);

  /// <summary>
  /// The number of matches won by the player.
  /// </summary>
  public int MatchWins => @base.MatchWins;

  /// <summary>
  /// The number of matches lost by the player.
  /// </summary>
  public int MatchLosses => @base.MatchLosses;
}
