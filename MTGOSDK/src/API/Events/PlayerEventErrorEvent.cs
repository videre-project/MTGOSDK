/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;


namespace MTGOSDK.API;
using static MTGOSDK.API.Play.EventManager;

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
  /// Delegate type for subscribing to PlayerEventError events.
  /// </summary>
  public delegate void PlayerEventErrorEventCallback(PlayerEventErrorEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on PlayerEventError events.
  /// </summary>
  public class PlayerEventErrorEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.PlayerEventErrorEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The match instance that triggered the event.
    /// </summary>
    public dynamic PlayerEvent => PlayerEventFactory(@base.PlayerEvent);

    /// <summary>
    /// The internal reason code for the exception.
    /// </summary>
    public int ReasonCode => @base.ReasonCode;

    /// <summary>
    /// The exception that triggered the event.
    /// </summary>
    public Exception Exception => Cast<Exception>(@base.Exception);
  }
}
