/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Tournaments;


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
  /// Delegate type for subscribing to Tournament error events.
  /// </summary>
  public delegate void TournamentErrorEventCallback(TournamentErrorEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Tournament error events.
  /// </summary>
  public class TournamentErrorEventArgs(dynamic args)
      : PlayerEventErrorEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The tournament instance that triggered the event.
    /// </summary>
    public Tournament Tournament => new(@base.Tournament);
  }
}
