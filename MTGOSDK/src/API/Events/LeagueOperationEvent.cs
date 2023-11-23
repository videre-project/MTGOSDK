/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.Leagues;
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
  /// Delegate type for League events executing an operation.
  /// </summary>
  public delegate void LeagueOperationEventCallback(LeagueOperationEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on League events executing an operation.
  /// </summary>
  public class LeagueOperationEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.LeagueOperationCompletedEventArgs<LeagueResponseEnum>>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The league instance that triggered the event.
    /// </summary>
    public League League => new(@base.League);

    /// <summary>
    /// The result of the operation.
    /// </summary>
    public LeagueResponseEnum Result =>
      Cast<LeagueResponseEnum>(@base.Result);
  }
}
