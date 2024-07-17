/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;


namespace MTGOSDK.API;
using static MTGOSDK.API.Play.Event<dynamic>;

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
  /// Delegate type for subscribing to PlayerEvent creation events.
  /// </summary>
  public delegate void PlayerEventsCreatedEventCallback(PlayerEventsCreatedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on PlayerEvent creation events.
  /// </summary>
  public class PlayerEventsCreatedEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.PlayerEventsCreatedEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The PlayerEvent instances that triggered the event.
    /// </summary>
    public IEnumerable<dynamic> Events =>
      Map<dynamic>(@base.Events, PlayerEventFactory);
  }
}
