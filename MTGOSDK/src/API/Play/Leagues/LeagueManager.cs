/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Leagues;
using static MTGOSDK.API.Events;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

public static class LeagueManager
{
  /// <summary>
  /// Global manager for all player events, including game joins and replays.
  /// </summary>
  private static readonly ILeaguesManager s_leagueManager =
    ObjectProvider.Get<ILeaguesManager>();

  public static readonly dynamic leaguesById =
    Unbind(s_leagueManager).m_leaguesById;

  private static readonly dynamic leaguesByToken =
    Unbind(s_leagueManager).m_leaguesByToken;

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

  //
  // ILeagueManager wrapper events
  //

  public static EventProxy<LeagueEventArgs> LeagueAdded =
    new(s_leagueManager, nameof(LeagueAdded));

  public static EventProxy<LeagueEventArgs> LeagueRemoved =
    new(s_leagueManager, nameof(LeagueRemoved));

  public static EventProxy<LeagueEventArgs> LocalUserJoinedLeague =
    new(s_leagueManager, nameof(LocalUserJoinedLeague));

  public static EventProxy<LeagueEventArgs> LocalUserLeftLeague =
    new(s_leagueManager, nameof(LocalUserLeftLeague));

  public static EventProxy<LeagueEventArgs> LeagueStateChanged =
    new(s_leagueManager, nameof(LeagueStateChanged));

  public static EventProxy<LeagueEventArgs> ReceivedLeagueList =
    new(s_leagueManager, nameof(ReceivedLeagueList));
}
