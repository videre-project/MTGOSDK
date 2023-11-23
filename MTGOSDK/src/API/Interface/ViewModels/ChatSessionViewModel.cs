/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Chat;
using MTGOSDK.Core.Reflection;

using Shiny.Chat;


namespace MTGOSDK.API.Interface.ViewModels;

public sealed class ChatSessionViewModel(dynamic chatSessionViewModel)
    : DLRWrapper<IChatSessionViewModel>
{
  /// <summary>
  /// Stores an internal reference to the IChatSessionViewModel object.
  /// </summary>
  internal override dynamic obj =>
    Bind<IChatSessionViewModel>(chatSessionViewModel);

  //
  // IChatSessionViewModel wrapper properties
  //

  /// <summary>
  /// The name of the channel.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The Channel object associated with this view model.
  /// </summary>
  public Channel Channel => new(@base.Channel);

  //
  // IChatSessionViewModel wrapper methods
  //

  /// <summary>
  /// Activates the channel's view model.
  /// </summary>
  public void Activate() => @base.Activate();

  /// <summary>
  /// Closes the channel's view model.
  /// </summary>
  /// <param name="leaveChannel">Whether to leave the channel.</param>
  public void Close(bool leaveChannel) => @base.Close(leaveChannel);

  /// <summary>
  /// Sends a message to the channel.
  /// </summary>
  /// <param name="message">The message to send.</param>
  public void Send(string message) => @base.SendCommand.Execute(message);

  //
  // IChatSessionViewModel wrapper events
  //

  public EventProxy Activated =
    new(/* IChatSessionViewModel */ chatSessionViewModel, nameof(Activated));

  public EventProxy ClearSendPane =
    new(/* IChatSessionViewModel */ chatSessionViewModel, nameof(ClearSendPane));

  public EventProxy StreamChanged =
    new(/* IChatSessionViewModel */ chatSessionViewModel, nameof(StreamChanged));
}
