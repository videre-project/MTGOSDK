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
  /// Delegate type for subscribing to MagicException error events.
  /// </summary>
  public delegate void ErrorEventCallback(ErrorEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on MagicException error events.
  /// </summary>
  public class ErrorEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.ErrorEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The MagicException that was thrown.
    /// </summary>
    public Exception Exception => Cast<Exception>(@base.Exception);
  }
}
