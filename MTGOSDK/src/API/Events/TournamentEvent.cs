/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


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
  /// Delegate type for subscribing to Tournament events.
  /// </summary>
  public delegate void TournamentEventCallback(TournamentEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Tournament events.
  /// </summary>
  public class TournamentEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.TournamentEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The tournament instance that triggered the event.
    /// </summary>
    public Tournament Tournament => new(@base.Tournament);
  }
}
