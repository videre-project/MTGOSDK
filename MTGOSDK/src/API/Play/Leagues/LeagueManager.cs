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

  private static dynamic leaguesById =>
    Unbind(s_leagueManager).m_leaguesById;

  // m_myLeagues
  // SuggestedLeagues

  /// <summary>
  /// All currently queryable League events.
  /// </summary>
  public static IEnumerable<League> Leagues => Map<League>(leaguesById.Values);

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
