/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;


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
  /// Delegate type for subscribing to Queue error events.
  /// </summary>
  public delegate void QueueErrorEventCallback(QueueErrorEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Queue error events.
  /// </summary>
  public class QueueErrorEventArgs(dynamic args)
      : PlayerEventErrorEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The queue instance that triggered the event.
    /// </summary>
    public Queue Queue => new(@base.Queue);
  }
}
