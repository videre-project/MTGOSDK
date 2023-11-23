/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play.Tournaments;


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
  /// Delegate type for Tournament events updating the tournament state.
  /// </summary>
  public delegate void TournamentStateChangedEventCallback(TournamentStateChangedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Tournament events updating the tournament state.
  /// </summary>
  public class TournamentStateChangedEventArgs(dynamic args)
      : TournamentEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The previous tournament state.
    /// </summary>
    public TournamentState OldValue => Cast<TournamentState>(@base.OldValue);

    /// <summary>
    /// The new tournament state.
    /// </summary>
    public TournamentState NewValue => Cast<TournamentState>(@base.NewValue);
  }
}
