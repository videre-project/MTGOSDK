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
  /// Delegate type for subscribing to Trade error events.
  /// </summary>
  public delegate void TradeErrorEventCallback(TradeErrorEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Trade error events.
  /// </summary>
  public class TradeErrorEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Trade.Events.TradeErrorEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The trade instance that triggered the event.
    /// </summary>
    public TradeEscrow Trade => new(@base.Trade);

    /// <summary>
    /// The error code associated with the event.
    /// </summary>
    public TradeError ErrorCode => Cast<TradeError>(@base.ErrorCode);
  }
}
