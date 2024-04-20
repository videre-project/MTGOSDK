/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play.History;


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
  /// Delegate type for subscribing to Replay error events.
  /// </summary>
  public delegate void ReplayErrorEventCallback(ReplayErrorEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Replay error events.
  /// </summary>
  public class ReplayErrorEventArgs(dynamic args) : ErrorEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The Replay instance that triggered the event.
    /// </summary>
    public Replay Replay => new(@base.ReplayGame);
  }
}
