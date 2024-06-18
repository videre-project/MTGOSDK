/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Leagues;
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
  /// Delegate type for subscribing to League events.
  /// </summary>
  public delegate void LeagueEventCallback(LeagueEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on League events.
  /// </summary>
  public class LeagueEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.LeagueEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The league instance that triggered the event.
    /// </summary>
    public League OldState => new (@base.League);
  }
}
