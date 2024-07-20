/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Settings;
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
  /// Delegate type for subscribing to Setting events.
  /// </summary>
  public delegate void SettingEventCallback(SettingEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Setting events.
  /// </summary>
  public class SettingEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.Settings.SettingEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The setting that triggered the event.
    /// </summary>
    public ISetting Setting => Bind<ISetting>(@base.Setting);
  }
}
