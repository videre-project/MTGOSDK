/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Specialized;

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK;
using GameCard = MTGOSDK.API.Play.GameCard;
using GameZone = MTGOSDK.API.Play.GameZone;

/// <summary>
/// EventHandler wrapper types used by the API.
/// </summary>
/// <remarks>
/// This class contains wrapper types for events importable via
/// <br/>
/// <c>using static MTGOSDK.API.Events;</c>.
/// </remarks>
public sealed partial class Events
{
  //
  // EventHandler delegate types
  //

  /// <summary>
  /// Delegate type for subscribing to GameZone events.
  /// </summary>
  public delegate void GameZoneEventCallback(GameZone zone, GameZoneEventArg args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on GameZone events.
  /// </summary>
  /// <remarks>
  /// This event is triggered by the type's NotifyCollectionChangedEventHandler:
  /// https://learn.microsoft.com/en-us/dotnet/api/system.collections.specialized.notifycollectionchangedeventhandler
  /// </remarks>
  public class GameZoneEventArg(dynamic args)
      : DLRWrapper<NotifyCollectionChangedEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// Gets the action that caused the event.
    /// </summary>
    public NotifyCollectionChangedAction Action =>
      Cast<NotifyCollectionChangedAction>(Unbind(@base).Action);

    /// <summary>
    /// Gets the list of new items involved in the change.
    /// </summary>
    public IEnumerable<GameCard> NewItems =>
      Map<GameCard>(Unbind(@base).NewItems);

    /// <summary>
    /// Gets the index at which the change occurred.
    /// </summary>
    public int NewStartingIndex => @base.NewStartingIndex;

    /// <summary>
    /// Gets the list of items affected by a Replace, Remove, or Move action.
    /// </summary>
    public IEnumerable<GameCard> OldItems =>
      Map<GameCard>(Unbind(@base).OldItems);

    /// <summary>
    /// Gets the index at which a Move, Remove, or Replace action occurred.
    /// </summary>
    public int OldStartingIndex => @base.OldStartingIndex;
  }
}
