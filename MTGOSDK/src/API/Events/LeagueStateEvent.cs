/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MTGO.Common;


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
  /// Delegate type for subscribing to League events updating the league state.
  /// </summary>
  public delegate void LeagueStateEventCallback(LeagueStateEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on League events updating the league state.
  /// </summary>
  public class LeagueStateEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.LeagueStateChangedEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The previous league state.
    /// </summary>
    public LeagueStateEnum OldState => Cast<LeagueStateEnum>(@base.OldValue);

    /// <summary>
    /// The new league state.
    /// </summary>
    public LeagueStateEnum NewState => Cast<LeagueStateEnum>(@base.NewValue);
  }
}
