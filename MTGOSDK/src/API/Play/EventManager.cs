/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using static MTGOSDK.API.Play.Event<dynamic>;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;
using static MTGOSDK.API.Events;

public static class EventManager
{
  /// <summary>
  /// Global manager for all player events, including game joins and replays.
  /// </summary>
  private static readonly IPlayerEventManager s_playerEventManager =
    ObjectProvider.Get<IPlayerEventManager>();

  /// <summary>
  /// Global manager for all player events, including game joins and replays.
  /// </summary>
  private static readonly IPlay s_playService =
    ObjectProvider.Get<IPlay>();

  //
  // IPlayerEvent wrapper methods
  //

  /// <summary>
  /// A dictionary of all events by their event ID.
  /// </summary>
  private static dynamic eventsById =>
    Unbind(s_playService).m_matchesAndTournamentsAndQueuesById;

  /// <summary>
  /// A dictionary of all events by their event token.
  /// </summary>
  private static dynamic eventsByToken =>
    Unbind(s_playerEventManager).m_openEventsByToken;

  /// <summary>
  /// All currently queryable events with GetEvent().
  /// </summary>
  public static IEnumerable<dynamic> Events =>
    Map<dynamic>(eventsById.Values, PlayerEventFactory);

  /// <summary>
  /// Retrieves an event by it's event ID.
  /// </summary>
  /// <param name="id">The event ID to query.</param>
  /// <returns>The event object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the event could not be found.
  /// </exception>
	public static dynamic GetEvent(int id) =>
    FromPlayerEvent((
      Unbind(s_playService).GetMatchOrTournamentOrQueueById(id)
        ?? throw new KeyNotFoundException($"Event #{id} could not be found.")
    ).PlayerEvent);

  /// <summary>
  /// Retrieves an event by it's event GUID.
  /// </summary>
  /// <param name="guid">The event GUID to query.</param>
  /// <returns>The event object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the event could not be found.
  /// </exception>
  public static dynamic GetEvent(Guid guid) =>
    FromPlayerEvent(
      Unbind(s_playerEventManager).GetEvent(guid)
        ?? throw new KeyNotFoundException($"Event could not be found.")
    );

  //
  // IPlayerEvent wrapper events
  //

  public static EventProxy<PlayerEventsCreatedEventArgs> PlayerEventsCreated =
    new(s_playerEventManager, nameof(PlayerEventsCreated));

  public static EventProxy<PlayerEventsRemovedEventArgs> PlayerEventsRemoved =
    new(s_playerEventManager, nameof(PlayerEventsRemoved));

  public static EventProxy<ReplayCreatedEventArgs> ReplayEventCreated =
    new(s_playerEventManager, nameof(ReplayEventCreated));

  public static EventProxy<ReplayErrorEventArgs> ReplayError =
    new(s_playerEventManager, nameof(ReplayError));
}
