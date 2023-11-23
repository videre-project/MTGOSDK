/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play.Enums;


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
  /// Delegate type for subscribing to Match events updating the match status.
  /// </summary>
  public delegate void MatchStatusEventCallback(MatchStatusEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Match events updating the match status.
  /// </summary>
  public class MatchStatusEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.MatchStatusEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The match instance that triggered the event.
    /// </summary>
    public Match Match => new(@base.Match);

    /// <summary>
    /// The previous match status.
    /// </summary>
    public MatchStatuses OldStatus => Cast<MatchStatuses>(@base.OldStatus);

    /// <summary>
    /// The new match status.
    /// </summary>
    public MatchStatuses NewStatus => Cast<MatchStatuses>(@base.NewStatus);
  }
}
