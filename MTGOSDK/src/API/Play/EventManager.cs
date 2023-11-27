/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.Core;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;
using static MTGOSDK.API.Events;
using static MTGOSDK.API.Play.Event<dynamic>;

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
  private static readonly dynamic s_playService =
    //
    // We must call the internal GetInstance() method to retrieve PropertyInfo
    // data from the remote type as the local proxy type or ObjectProvider will
    // restrict access to internal or private members.
    //
    // This is a limitation of the current implementation of the Proxy<T> type
    // since any MemberInfo data is cached by the runtime and will conflict
    // with RemoteNET's internal type reflection methods.
    //
    RemoteClient.GetInstance("WotC.MtGO.Client.Model.Play.PlayService");

  //
  // IPlayerEvent wrapper methods
  //

  /// <summary>
  /// A dictionary of all events by their event ID.
  /// </summary>
  private static dynamic eventsById =>
    s_playService.m_matchesAndTournamentsAndQueuesById;

  /// <summary>
  /// All currently queryable events with GetEvent().
  /// </summary>
  public static IEnumerable<dynamic> Events
  {
    get
    {
      foreach (var playerEvent in eventsById.Values)
        yield return FromPlayerEvent(playerEvent);
    }
  }

  /// <summary>
  /// Retrieves a joinable event by it's event ID.
  /// </summary>
  /// <param name="id">The event ID to query.</param>
  /// <returns>The event object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the event could not be found.
  /// </exception>
  public static dynamic GetJoinableEvent(int id) =>
    FromPlayerEvent((
      s_playService.GetFilterablePlayerEventById(id)
        ?? throw new KeyNotFoundException($"Event #{id} could not be found.")
    ).PlayerEvent);

  /// <summary>
  /// Retrieves a joinable event by it's event GUID.
  /// </summary>
  /// <param name="guid">The event GUID to query.</param>
  /// <returns>The event object.</returns>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the event could not be found.
  /// </exception>
  public static dynamic GetJoinableEvent(Guid guid) =>
    FromPlayerEvent((
      s_playService.GetFilterablePlayerEventByGuid(guid)
        ?? throw new KeyNotFoundException($"Event could not be found.")
    ).PlayerEvent);

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
      s_playService.GetMatchOrTournamentOrQueueById(id)
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
      s_playerEventManager.GetEvent(guid)
        ?? throw new KeyNotFoundException($"Event could not be found.")
    );

  //
  // IPlayerEvent wrapper events
  //

  public static EventProxy<PlayerEventsCreatedEventArgs> PlayerEventsCreated =
    new(s_playerEventManager);

  public static EventProxy<PlayerEventsRemovedEventArgs> PlayerEventsRemoved =
    new(s_playerEventManager);

  public static EventProxy<ReplayCreatedEventArgs> ReplayEventCreated =
    new(s_playerEventManager);

  public static EventProxy<ReplayErrorEventArgs> ReplayError =
    new(s_playerEventManager);
}
