/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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
  /// Delegate type for Tournament events updating the current round.
  /// </summary>
  public delegate void TournamentRoundChangedEventCallback(TournamentRoundChangedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event arg triggered when the current round of a tournament changes.
  /// </summary>
  public class TournamentRoundChangedEventArgs(dynamic args)
      : TournamentEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The previous round of results in the tournament.
    /// </summary>
    public TournamentRoundChangedEventArgs PreviousRound =>
      new(@base.PreviousRound);

    /// <summary>
    /// The current round of results in the tournament.
    /// </summary>
    public TournamentRoundChangedEventArgs CurrentRound =>
      new(@base.CurrentRound);
  }
}
