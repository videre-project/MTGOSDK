/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
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
  /// Delegate type for subscribing to Chat session events.
  /// </summary>
  public delegate void ChatSessionEventCallback(ChatSessionEventArgs args);

  //
  // EventHandler argument types
  //

  /// <summary>
  /// Event args triggered on Chat session events.
  /// </summary>
  public class ChatSessionEventArgs(dynamic args)
      : DLRWrapper<Shiny.Chat.ChatSessionEventArgs>
  {
    internal override dynamic obj => args;

    /// <summary>
    /// The session view model instance that triggered the event.
    /// </summary>
    public ChatSessionViewModel Session => new(@base.Session);
  }
}
