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
  /// Delegate type for subscribing to Queue events updating the queue state.
  /// </summary>
  public delegate void QueueStateEventCallback(QueueStateEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Queue events updating the queue state.
  /// </summary>
  public class QueueStateEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.QueueStateEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The queue instance that triggered the event.
    /// </summary>
    public Queue Queue => new(@base.Queue);

    /// <summary>
    /// The previous queue state.
    /// </summary>
    public QueueState OldState => Cast<QueueState>(@base.GameState);

    /// <summary>
    /// The new queue state.
    /// </summary>
    public QueueState NewState => Cast<QueueState>(@base.GameState);
  }
}
