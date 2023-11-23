/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Chat;
using MTGOSDK.Core.Reflection;


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
  /// Delegate type for subscribing to Channel events.
  /// </summary>
  public delegate void ChannelEventCallback(ChannelEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Channel events.
  /// </summary>
  public class ChannelEventArgs(dynamic args) : DLRWrapper<dynamic>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The channel instance that triggered the event.
    /// </summary>
    public Channel Channel => new(@base.Channel);
  }
}
