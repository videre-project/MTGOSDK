/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Extensions;
using static MTGOSDK.Core.Reflection.DLRWrapper;
using MTGOSDK.Core.Remoting.Types;

using WotC.MtGO.Client.Model.Play;
using WotC.MtGO.Client.Model.Play.Filters;
using WotC.MtGO.Client.Model.Play.Tournaments;


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
  // IPlayerEvent wrapper properties
  //

  /// <summary>
  /// A dictionary of all events by their event ID.
  /// </summary>
  private static dynamic m_eventsById =>
    field ??= Unbind(s_playService).m_matchesAndTournamentsAndQueuesById;

  private static dynamic m_sortedEventsById =>
    ((DynamicRemoteObject)m_eventsById.Values)
      .Filter<IPlayerEvent>(e => e.EventId != -1)
      .Sort<IPlayerEvent, int>(e => e.EventId);

  /// <summary>
  /// All currently queryable events with GetEvent().
  /// </summary>
  /// <remarks>
  /// This property is a dynamic collection of all events, including matches,
  /// tournaments, and queues. Traversing the whole collection may often take
  /// 30 seconds to a minute to complete, depending on the number of events.
  /// </remarks>
  public static IEnumerable<dynamic> Events =>
    Map<dynamic>(m_sortedEventsById, PlayerEventFactory);

  private static dynamic m_featuredEvents =>
    field ??= ((DynamicRemoteObject)
        Unbind(s_playService).GetFeaturedFilterables())
          .Filter<IPlayerEvent>(e => e.MinimumPlayers > 2)
          .Sort<ITournament, DateTime>(e => e.ScheduledStartTime);

  /// <summary>
  /// All currently scheduled tournaments queryable with GetEvent().
  /// </summary>
  /// <remarks>
  /// This property is a dynamic collection of all tournaments that are
  /// currently scheduled. Traversing the whole collection may often take
  /// 10-20 seconds to complete, depending on the number of tournaments.
  /// </remarks>
  public static IEnumerable<Tournament> FeaturedEvents =>
    Map(m_featuredEvents, Lambda<Tournament>(e => new(e.PlayerEvent)));

  public static int FeaturedEventsCount => m_featuredEvents.Count;

  private static dynamic m_joinedEvents =>
    field ??= Unbind(s_playService).JoinedEvents;

  /// <summary>
  /// All joined events that the player is currently participating in.
  /// </summary>
  public static IEnumerable<dynamic> JoinedEvents =>
    Map<dynamic>(m_joinedEvents, PlayerEventFactory);

  public static int JoinedEventsCount => m_joinedEvents.Count;

  //
  // Batch serialization methods
  //

  /// <summary>
  /// Specifies which event collection to serialize.
  /// </summary>
  public enum EventCollection
  {
    /// <summary>All events (sorted by EventId)</summary>
    All,
    /// <summary>Featured tournaments (scheduled events)</summary>
    Featured,
    /// <summary>Events the player has joined</summary>
    Joined
  }

  /// <summary>
  /// Serializes events using cross-event batch fetching for primitive properties.
  /// </summary>
  /// <typeparam name="TInterface">The interface type to serialize events as.</typeparam>
  /// <param name="collection">Which event collection to serialize.</param>
  /// <param name="maxItems">Maximum number of items to serialize (0 = no limit).</param>
  /// <returns>Enumerable of serialized events implementing TInterface.</returns>
  /// <remarks>
  /// This method uses a single IPC call to fetch all items' primitive properties,
  /// avoiding per-item overhead. Complex properties are still fetched per-item.
  /// </remarks>
  public static IEnumerable<TInterface> SerializeEventsAs<TInterface>(
    EventCollection collection = EventCollection.All,
    int maxItems = 0)
    where TInterface : class
  {
    // Select the underlying DRO based on collection type
    DynamicRemoteObject? dro = collection switch
    {
      EventCollection.All      => (DynamicRemoteObject)m_sortedEventsById,
      EventCollection.Featured => (DynamicRemoteObject)m_featuredEvents,
      EventCollection.Joined   => (DynamicRemoteObject)m_joinedEvents,
      _ => null
    };
    if (dro == null)
      throw new ArgumentException("Invalid event collection.");

    // Use batch serialization with Tournament as path source
    return SerializeDroAs<TInterface, Tournaments.Tournament>(
      dro,
      pathPrefix: nameof(IFilterablePlayerEvent.PlayerEvent),
      maxItems);
  }

  //
  // IPlayerEvent wrapper methods
  //

  /// <summary>
  /// Retrieves an event by it's event ID.
  /// </summary>
  /// <param name="id">The event ID to query.</param>
  /// <returns>The event object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the event ID is less than or equal to 0.
  /// </exception>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the event could not be found.
  /// </exception>
	public static dynamic GetEvent(int id)
  {
    if (id <= 0)
      throw new ArgumentException("Event ID must be greater than 0.");

    dynamic playerEvent = Unbind(s_playService).GetMatchOrTournamentOrQueueById(id)
      ?? throw new KeyNotFoundException($"Event #{id} could not be found.");

    return FromPlayerEvent(playerEvent);
  }

  /// <summary>
  /// Retrieves an event by it's event GUID.
  /// </summary>
  /// <param name="guid">The event GUID to query.</param>
  /// <returns>The event object.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown if the event GUID is empty.
  /// </exception>
  /// <exception cref="KeyNotFoundException">
  /// Thrown if the event could not be found.
  /// </exception>
  public static dynamic GetEvent(Guid guid)
  {
    if (guid == Guid.Empty)
      throw new ArgumentException("Event GUID must not be empty.");

    dynamic playerEvent = Unbind(s_playerEventManager).GetEvent(guid)
      ?? throw new KeyNotFoundException($"Event ({guid}) could not be found.");

    return FromPlayerEvent(playerEvent);
  }

  /// <summary>
  /// Retrieves the parent event of a match from a collection of event objects.
  /// </summary>
  /// <param name="events">The collection of event objects to search.</param>
  /// <param name="match">The match object to find the parent event for.</param>
  /// <returns>The parent event of the game, or null if not found.</returns>
  public static Event? FindParentEvent(IEnumerable<dynamic> events, Match match)
  {
    int matchId = match.Id;
    foreach(var playerEvent in events)
    {
      switch (playerEvent)
      {
        case League league:
          if (league.GameHistory.Any(e => e.MatchId == matchId))
            return league;
          break;
        case Tournament tournament:
          foreach (var round in tournament.Rounds)
          {
            if (round.Matches.Any(m => m.Id == matchId))
              return tournament;
          }
          break;
        case Match matchEvent:
          if (matchEvent.Id == matchId)
            return matchEvent;
          break;
        case Queue queue:
          break;
      }
    }

    return null;
  }

  /// <summary>
  /// Navigates to and opens the event in the client.
  /// </summary>
  /// <param name="id">The event ID to navigate to.</param>
  public static void NavigateToEvent(int id)
  {
    var playerEvent = GetEvent(id);
    using var viewModel = new BasicToastViewModel("", ""); // Dummy viewmodel
    viewModel.SetNavigateToViewCommand(playerEvent);
    viewModel.ExecuteViewCommand();
  }

  public static readonly Func<dynamic, Event> PlayerEventFactory =
    new(FromPlayerEvent);

  private static Event FromPlayerEvent(dynamic playerEvent)
  {
    // If an event is provided as a FilterableEvent, extract the actual event.
    string eventType = playerEvent.GetType().Name;
    dynamic eventObject = eventType.StartsWith("Filterable")
      ? playerEvent.PlayerEvent
      : playerEvent;

    //
    // Here, we encountered a null PlayerEvent object extracted from a
    // FilterablePlayerEvent (or the input playerEvent was simply null).
    //
    // This is likely an object that should have been garbage collected
    // but wasn't due to a strong reference elsewhere (a memory leak).
    //
    if (eventObject == null)
      throw new InvalidOperationException("PlayerEvent object is null.");

    // Map each event type to its corresponding wrapper class.
    switch (eventType)
    {
      case "FilterableLeague" or "League":
        eventObject = new League(eventObject);
        break;
      case "FilterableMatch" or "Match" or
           "LeagueMatch" or "TournamentMatch":
        eventObject = new Match(eventObject);
        break;
      case "FilterableTournament" or "Tournament":
        eventObject = new Tournament(eventObject);
        break;
      case "FilterableQueue" or "Queue" or
           "TournamentQueue":
        eventObject = new Queue(eventObject);
        break;
      default:
        throw new InvalidOperationException($"Unknown event type: {eventType}");
    }
    Log.Trace("Created new {Type} object for '{EventObject}'.",
        eventObject.GetType().Name, eventObject);

    return eventObject;
  }

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

  //
  // IPlayService static events
  //

  /// <summary>
  /// Event triggered when a new event is joined by the user.
  /// </summary>
  public static EventHookProxy<Event, object> EventJoined =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.PlayService>(),
      "AddJoinedEvent",
      new((_, args) =>
      {
        var playerEvent = PlayerEventFactory(args[0]);
        return (playerEvent, null); // Return a tuple of (Event, null).
      })
    );

  /// <summary>
  /// Event triggered when a new game is joined by the user.
  /// </summary>
  /// <remarks>
  /// This event is triggered when the user joins a game (or a new game starts).
  /// </remarks>
  public static EventHookProxy<Event, Game> GameJoined =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Play.PlayService>(),
      "OnGameStarted",
      new((_, args) =>
      {
        Game game = new(args[0]);

        Match match = game.Match;
        Event? playerEvent = FindParentEvent(JoinedEvents, match);
        if (playerEvent == null)
          return null; // Ignore no-op or invalid events.

        return (playerEvent, game); // Return a tuple of (Event, Game).
      })
    );
}
