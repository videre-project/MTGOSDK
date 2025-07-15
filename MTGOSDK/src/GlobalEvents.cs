/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Logging;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK;

public static class GlobalEvents
{
  public delegate bool Filter(dynamic obj, dynamic args);

  public delegate dynamic Map(dynamic obj, dynamic args);

  public record GlobalEvent(
    string TypeName,
    string EventName,
    Filter Filter,
    Map? Map = null);

  public class EventConfiguration : Dictionary<(string, string), (Filter?, Map?)>
  {
    public EventConfiguration(params IEnumerable<GlobalEvent> events)
    {
      foreach (var e in events)
      {
        this[(e.TypeName, e.EventName)] = (e.Filter, e.Map);
      }
    }
  }

  //
  // Server-side event configuration.
  //

  public static readonly EventConfiguration Events = new(
    //
    // MTGOSDK.API.Play.EventManager
    //
    new GlobalEvent(
      "WotC.MtGO.Client.Model.Play.PlayService",
      "AddJoinedEvent",
      // Ignore no-op or duplicate events
      (_, a) => a[0] != null && a[0].IsLocalUserJoined),
    new GlobalEvent(
      "WotC.MtGO.Client.Model.Play.PlayService",
      "OnGameStarted",
      // Ignore no-op or duplicate events
      (_, a) => a[0] != null && a[0].Game != null,
      (_, a) => new dynamic[] { a[0].Game }),
    //
    // MTGOSDK.API.Play.Match
    //
    new GlobalEvent(
      "WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase",
      "OnMatchStatusChanged",
      // Ignore non-local matches
      (o, _) => (/* IMatch */ o).IsLocalUserParticipant),
    new GlobalEvent(
      "WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase",
      "OnCurrentGameChanged",
      // Ignore invalid game objects and non-local matches
      (o, _) => (/* IMatch */ o).IsLocalUserParticipant),
    new GlobalEvent(
      "WotC.MtGO.Client.Model.Play.MatchEvent.MatchBase",
      "OnGameEnded",
      // Ignore invalid games and non-local matches
      (o, _) => (/* IMatch */ o).IsLocalUserParticipant),
    //
    // MTGOSDK.API.Play.Games.Game
    //
    new GlobalEvent(
      "Shiny.Play.Duel.ViewModel.PhaseControllerViewModel",
      "set_CurrentPhase",
      null,
      (o, a) => new dynamic[] { o.ActivePlayer, a[0].GamePhase }),
    new GlobalEvent(
      "WotC.MtGO.Client.Model.Play.GameCard",
      "OnZoneChanged",
      // Ignore zone change events from creating game cards on the client
      (o, _) => (/* IGameCard */ o).SourceId != -1)
  );

  public static bool IsValidEvent(
    (string TypeName, string EventName) eventKey,
    dynamic obj,
    dynamic args,
    out dynamic? mappedArgs)
  {
    if (Events.TryGetValue(eventKey, out var t))
    {
      var (filter, map) = t;
      if (filter != null && !Try(() => filter(obj, args)))
      {
        mappedArgs = null;
        return false; // Filter did not pass.
      }

      if (map != null)
      {
        try
        {
          mappedArgs = map(obj, args);
        }
        catch (Exception ex)
        {
          Log.Error("Failed to map event arguments: {0}", ex.Message);
          Log.Debug("Event mapping error: {0}", ex);
          mappedArgs = null;
        }
      }
      else mappedArgs = args; // No mapping function, use original args.
    }
    else
    {
      mappedArgs = args;
    }

    return true;
  }
}
