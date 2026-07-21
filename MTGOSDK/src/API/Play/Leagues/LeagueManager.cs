/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.Core.Reflection.DLRWrapper;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Leagues;
using static MTGOSDK.API.Events;

public static class LeagueManager
{
  /// <summary>
  /// Global manager for all player events, including game joins and replays.
  /// </summary>
  private static readonly ILeaguesManager s_leagueManager =
    ObjectProvider.Get<ILeaguesManager>();

  static LeagueManager()
  {
    ObjectCache.OnReset += delegate
    {
      leaguesById = null;
      leaguesByToken = null;
    };
  }

  public static dynamic leaguesById
  {
    get => field ??= Unbind(s_leagueManager).m_leaguesById;
    set => field = value;
  }

  private static dynamic leaguesByToken
  {
    get => field ??= Unbind(s_leagueManager).m_leaguesByToken;
    set => field = value;
  }

  /// <summary>
  /// All currently queryable League events.
  /// </summary>
  public static IEnumerable<League> Leagues => Map<League>(leaguesById.Values);

  /// <summary>
  /// The current user's open leagues.
  /// </summary>
  public static IEnumerable<League> OpenLeagues =>
    Map<League>(Unbind(s_leagueManager).m_myLeagues);

  //
  // ILeagueManager wrapper methods
  //

  public static League GetLeague(int id) =>
    leaguesById.ContainsKey(id)
      ? new League(leaguesById[id])
      : throw new KeyNotFoundException($"No league found with id {id}");

  public static League GetLeague(Guid guid) =>
    leaguesByToken.ContainsKey(guid)
      ? new League(leaguesByToken[guid])
      : throw new KeyNotFoundException($"No league found with guid {guid}");
}
