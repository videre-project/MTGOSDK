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
  /// Delegate type for subscribing to Toast request events.
  /// </summary>
  public delegate void ToastEventCallback(ToastEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Toast request events.
  /// </summary>
  public class ToastEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.ToastRequestEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The title of the toast.
    /// </summary>
    public string Title => @base.Header;

    /// <summary>
    /// The message text of the toast.
    /// </summary>
    public string Text => @base.Message;

    /// <summary>
    /// Whether the toast is shown forever.
    /// </summary>
    public bool ShowForever => @base.IsPersistent;
  }
}
