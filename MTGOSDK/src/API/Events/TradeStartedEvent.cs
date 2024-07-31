/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Trade;
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
  /// Delegate type for subscribing to Trade events.
  /// </summary>
  public delegate void TradeStartedEventCallback(TradeStartedEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Trade events.
  /// </summary>
  public class TradeStartedEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Trade.Events.TradeStartedEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The trade instance that triggered the event.
    /// </summary>
    public TradeEscrow Trade => new(@base.Trade);

    /// <summary>
    /// Whether the trade is currently pending.
    /// </summary>
    public bool IsPending => @base.IsTrade;
  }
}
