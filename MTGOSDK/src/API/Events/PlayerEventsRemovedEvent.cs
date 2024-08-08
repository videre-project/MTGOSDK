/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;


namespace MTGOSDK.API;

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
  /// Delegate type for subscribing to PlayerEvent removal events.
  /// </summary>
  public delegate void PlayerEventsRemovedEventCallback(PlayerEventsRemovedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on PlayerEvent removal events.
  /// </summary>
  public class PlayerEventsRemovedEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.PlayerEventsRemovedEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The PlayerEvent instances that triggered the event.
    /// </summary>
    public IEnumerable<dynamic> Events =>
      Map<dynamic>(@base.Events, EventManager.PlayerEventFactory);
  }
}
