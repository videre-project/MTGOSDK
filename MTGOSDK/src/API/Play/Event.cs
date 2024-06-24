/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Users;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;
using static MTGOSDK.API.Events;

public abstract class Event<I> : DLRWrapper<IPlayerEvent>
{
  //
  // IPlayerEvent wrapper properties
  //

  /// <summary>
  /// The event's tournament ID.
  /// </summary>
  public int Id => @base.EventId;

  /// <summary>
  /// The event's session token.
  /// </summary>
  public Guid Token => Cast<Guid>(Unbind(@base).EventToken);

  /// <summary>
  /// The event type (e.g. League, Tournament, Match, etc.).
  /// </summary>
  public string EventType => this.GetType().Name;

  /// <summary>
  /// A class describing the event format (e.g. Standard, Modern, Legacy, etc.).
  /// </summary>
  public PlayFormat Format => new(@base.PlayFormat);

  /// <summary>
  /// The name of this event (e.g. "Standard Preliminary").
  /// </summary>
  public string Description => @base.Description;

  /// <summary>
  /// The total number of players registered for the event.
  /// </summary>
  public int TotalPlayers => @base.JoinedUsers.Count;

  /// <summary>
  /// The current players registered for the event.
  /// </summary>
  public IEnumerable<User> Players => Map<User>(@base.JoinedUsers);

  /// <summary>
  /// The user's registered deck for the event.
  /// </summary>
  public Deck? RegisteredDeck => Optional<Deck>(@base.DeckUsedToJoin);

  /// <summary>
  /// The number of minutes each player has in each match.
  /// </summary>
  public int MinutesPerPlayer => @base.MinutesPerPlayer;

  /// <summary>
  /// The minimum number of players required to start the event.
  /// </summary>
  public int MinimumPlayers => @base.MinimumPlayers;

  /// <summary>
  /// The maximum number of players allowed to join the event.
  /// </summary>
  public int MaximumPlayers => @base.MaximumPlayers;

  /// <summary>
  /// Whether the event has ended.
  /// </summary>
  public bool IsCompleted => @base.IsCompleted;

  /// <summary>
  /// Whether the event has finished and is no longer active.
  /// </summary>
  public bool IsRemoved => @base.WasEventRemovedFromSystem;

  /// <summary>
  /// Whether the user has joined the event.
  /// </summary>
  public bool HasJoined => @base.IsLocalUserJoined;

  /// <summary>
  /// Whether the user is currently participating in the event.
  /// </summary>
  public bool IsParticipant => @base.IsLocalUserParticipant;

  /// <summary>
  /// Whether the user has been eliminated from the event.
  /// </summary>
  public bool IsEliminated => @base.IsLocalUserEliminated;

  //
  // IPlayerEvent wrapper methods
  //

  internal static Func<dynamic, dynamic> PlayerEventFactory = new(FromPlayerEvent);

  internal static dynamic FromPlayerEvent(dynamic playerEvent)
  {
    // If an event is provided as a FilterableEvent, extract the actual event.
    string eventType = playerEvent.GetType().Name;
    dynamic eventObject = eventType.StartsWith("Filterable")
      ? playerEvent.PlayerEvent
      : playerEvent;

    // Map each event type to its corresponding wrapper class.
    switch (eventType)
    {
      // case "FilterableLeague" or "League":
      //   eventObject = new League(eventObject);
      //   break;
      case "FilterableMatch" or "Match":
        eventObject = new Match(eventObject);
        break;
      case "FilterableTournament" or "Tournament":
        eventObject = new Tournament(eventObject);
        break;
      case "FilterableQueue" or "Queue":
        eventObject = new Queue(eventObject);
        break;
    }
    Log.Trace("Created new {Type} object for '{EventObject}'.",
        eventObject.GetType().Name, eventObject);

    return eventObject;
  }

  public override string ToString() => $"{Description} #{Id}";
}
