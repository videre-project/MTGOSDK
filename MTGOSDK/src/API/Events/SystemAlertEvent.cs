/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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
  /// Delegate type for subscribing to system alert events.
  /// </summary>
  public delegate void SystemAlertEventCallback(SystemAlertEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on system alert events.
  /// </summary>
  public class SystemAlertEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.SystemAlertEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The system alert message.
    /// </summary>
    public string Message => @base.Message;

    /// <summary>
    /// When the alert was triggered.
    /// </summary>
    public DateTime Timestamp => @base.Timestamp;

    /// <summary>
    /// Whether the alert is a league alert.
    /// </summary>
    public bool IsLeagueAlert => @base.IsLeagueAlert;
  }
}
