/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Trade;
using MTGOSDK.API.Trade.Enums;
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
  /// Delegate type for subscribing to changes in trade state.
  /// </summary>
  public delegate void TradeStateChangedEventCallback(TradeStateChangedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on a change in trade state.
  /// </summary>
  public class TradeStateChangedEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.Events.TournamentEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The trade instance that triggered the event.
    /// </summary>
    public TradeEscrow Trade => new(@base.Trade);

    /// <summary>
    /// The previous state of the trade.
    /// </summary>
    public TradeState OldState => Cast<TradeState>(@base.OldState);

    /// <summary>
    /// The new state of the trade.
    /// </summary>
    public TradeState NewState => Cast<TradeState>(@base.NewState);
  }
}
