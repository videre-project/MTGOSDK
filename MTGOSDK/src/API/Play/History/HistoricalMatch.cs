/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Users;

using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Represents a historical match.
/// </summary>
public sealed class HistoricalMatch(dynamic historicalMatch)
    : HistoricalItem<Match>
{
  /// <summary>
  /// Stores an internal reference to the IHistoricalMatch object.
  /// </summary>
  internal override dynamic obj => Bind<IHistoricalMatch>(historicalMatch);

  //
  // IHistoricalMatch wrapper properties
  //

  /// <summary>
  /// The opponents' player objects.
  /// </summary>
  public IList<User> Opponents =>
    field ??= Map<IList, User>(Unbind(@base).Opponents);

  /// <summary>
  /// The game IDs for the match.
  /// </summary>
  public IList<int> GameIds =>
    field ??= Map<IList, int>(@base.GameIds);

  /// <summary>
  /// The number of games won by the player.
  /// </summary>
  public int GameWins => @base.GameWins;

  /// <summary>
  /// The number of games lost by the player.
  /// </summary>
  public int GameLosses => @base.GameLosses;

  /// <summary>
  /// The number of games tied by the player.
  /// </summary>
  public int GameTies => GameIds.Count - (GameWins + GameLosses);
}
