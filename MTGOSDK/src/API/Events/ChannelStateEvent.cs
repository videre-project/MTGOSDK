/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Chat.Enums;


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
  /// Delegate type for subscribing to Channel events updating the channel state.
  /// </summary>
  public delegate void ChannelStateEventCallback(ChannelStateEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Channel events updating the channel state.
  /// </summary>
  public class ChannelStateEventArgs(dynamic args) : ChannelEventArgs(null)
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The previous channel state.
    /// </summary>
    public ChannelState OldState => Cast<ChannelState>(@base.ChannelState);

    /// <summary>
    /// The new channel state.
    /// </summary>
    public ChannelState NewState => Cast<ChannelState>(@base.ChannelState);
  }
}
