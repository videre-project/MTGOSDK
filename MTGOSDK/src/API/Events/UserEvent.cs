/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
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
  /// Delegate type for subscribing to User events.
  /// </summary>
  public delegate void UserEventCallback(UserEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on User events.
  /// </summary>
  public class UserEventArgs(dynamic args)
      : DLRWrapper<WotC.MtGO.Client.Model.UserEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The User instance that triggered the event.
    /// </summary>
    public User User => new(@base.User);
  }
}
