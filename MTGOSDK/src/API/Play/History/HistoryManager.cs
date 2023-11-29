/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Settings;


namespace MTGOSDK.API.Play.History;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

/// <summary>
/// Manager for the player's game history.
/// </summary>
public static class HistoryManager
{
  //
  // IGameHistoryManager wrapper methods
  //

  /// <summary>
  /// Global manager for the player's game history.
  /// </summary>
  private static IGameHistoryManager s_gameHistoryManager =
    ObjectProvider.Get<ISettings>().GameHistoryManager;

  /// <summary>
  /// Whether or not the game history has been loaded by the client.
  /// </summary>
  public static bool HistoryLoaded => s_gameHistoryManager.HistoryLoaded;

  /// <summary>
  /// The historical items (games, matches, tournaments, etc.) for the player.
  /// </summary>
  public static IEnumerable<dynamic> Items =>
    Map<dynamic>(
      s_gameHistoryManager.Items,
      new Func<dynamic, dynamic>(item =>
      {
        var type = item.GetType().Name;
        switch (type)
        {
          case "HistoricalItem":
            return new HistoricalItem<IHistoricalItem, dynamic>.Default(item);
          case "HistoricalMatch":
            return new HistoricalMatch(item);
          case "HistoricalTournament":
            return new HistoricalTournament(item);
          default:
            throw new NotImplementedException($"Unsupported type: {type}");
        }
      }));
}
