/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


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
  /// Delegate type for subscribing to Countdown events.
  /// </summary>
  public delegate void CountdownEventCallback(CountdownEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on a Countdown event.
  /// </summary>
  public class CountdownEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Play.CountdownStartedEventArgs>
  {
    internal override dynamic obj => args;

    public int TimeUntilGameStarts => @base.TimeUntilGameStarts;
  }
}
