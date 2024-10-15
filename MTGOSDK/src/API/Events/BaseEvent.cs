/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API;

/// <summary>
/// EventHandler wrapper callback and event arg types used by the API.
/// </summary>
/// <remarks>
/// This class contains wrapper types for MTGO's event callbacks.
/// For example, to subscribe to the <see cref="MTGOSDK.API.Client"/>'s
/// <see cref="MTGOSDK.API.Client.LogOnFailed"/> event (of type <see cref="MTGOSDK.Core.Reflection.Proxy.EventProxy{T}"/> whose generic argument <c>T</c> is <see cref="MTGOSDK.API.Events.ErrorEventArgs"/>),
/// you can use:
/// <code>
/// using static MTGOSDK.API.Events;
///
/// using client = new Client();
/// client.LogOnFailed += new ErrorEventCallback(args => {
///   Console.WriteLine($"Method A: Logon failed: {args.Message}");
/// });
/// </code>
/// This can also be done by creating an event callback manually:
/// <code>
/// client.LogOnFailed += new EventCallback&lt;ErrorEventArgs&gt;(args => {
///   Console.WriteLine($"Method B: Logon failed: {args.Exception}");
/// });
/// </code>
/// <para/>
/// <h2>Reference:</h2>
/// <see cref="Events.EventCallback{T}"/><br/>
/// <see cref="Events.CardGroupingItemsChangedEventCallback"/><br/>
/// <see cref="Events.ChannelEventCallback"/><br/>
/// <see cref="Events.ChannelStateEventCallback"/><br/>
/// <see cref="Events.ChatSessionEventCallback"/><br/>
/// <see cref="Events.CountdownEventCallback"/><br/>
/// <see cref="Events.ErrorEventCallback"/><br/>
/// <see cref="Events.GameCardEventCallback"/><br/>
/// <see cref="Events.GameEventCallback"/><br/>
/// <see cref="Events.GamePlayerEventCallback"/><br/>
/// <see cref="Events.GameStateEventCallback"/><br/>
/// <see cref="Events.GameZoneEventCallback"/><br/>
/// <see cref="Events.LeagueEventCallback"/><br/>
/// <see cref="Events.LeagueOperationEventCallback"/><br/>
/// <see cref="Events.LeagueStateEventCallback"/><br/>
/// <see cref="Events.MatchErrorEventCallback"/><br/>
/// <see cref="Events.MatchStatusEventCallback"/><br/>
/// <see cref="Events.PlayerEventErrorEventCallback"/><br/>
/// <see cref="Events.PlayerEventsCreatedEventCallback"/><br/>
/// <see cref="Events.PlayerEventsRemovedEventCallback"/><br/>
/// <see cref="Events.QueueErrorEventCallback"/><br/>
/// <see cref="Events.QueueStateEventCallback"/><br/>
/// <see cref="Events.ReplayCreatedEventCallback"/><br/>
/// <see cref="Events.ReplayErrorEventCallback"/><br/>
/// <see cref="Events.SettingEventCallback"/><br/>
/// <see cref="Events.SystemAlertEventCallback"/><br/>
/// <see cref="Events.ToastEventCallback"/><br/>
/// <see cref="Events.TournamentEventCallback"/><br/>
/// <see cref="Events.TournamentErrorEventCallback"/><br/>
/// <see cref="Events.TournamentRoundChangedEventCallback"/><br/>
/// <see cref="Events.TournamentStateChangedEventCallback"/><br/>
/// <see cref="Events.TradeErrorEventCallback"/><br/>
/// <see cref="Events.TradeStartedEventCallback"/><br/>
/// <see cref="Events.TradeStateChangedEventCallback"/><br/>
/// <see cref="Events.UserEventCallback"/><br/>
/// </remarks>
public sealed partial class Events
{
  //
  // EventHandler delegate types
  //

  /// <summary>
  /// Delegate type for subscribing to a generic event type.
  /// </summary>
  public delegate void EventCallback<T>(T args) where T : class;
}
