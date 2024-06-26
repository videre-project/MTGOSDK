/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Games;
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
  /// Delegate type for subscribing to Game events.
  /// </summary>
  public delegate void GameEventCallback(GameEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Game events.
  /// </summary>
  public class GameEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.GameEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The game instance that triggered the event.
    /// </summary>
    public Game Game => new(@base.Game);
  }
}
