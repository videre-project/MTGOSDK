/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.ViewModels;
using MTGOSDK.Core.Reflection;

using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;
using static MTGOSDK.API.Events;
// using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

public static class ChannelManager
{
  //
  // IChannelManager wrapper methods
  //

  /// <summary>
  /// Manages the client's set information and card definitions.
  /// </summary>
  private static readonly IChannelManager s_channelManager =
    ObjectProvider.Get<IChannelManager>();

  /// <summary>
  /// Gets the channel with the given ID.
  /// </summary>
  /// <param name="id">The ID of the channel to get.</param>
  /// <returns>A new channel object.</returns>
  public static Channel GetChannel(int id) =>
    new(s_channelManager.GetChannelById(id));

  /// <summary>
  /// Gets the channel with the given name.
  /// </summary>
  /// <param name="name">The name of the channel to get.</param>
  /// <returns>A new channel object.</returns>
  public static Channel GetChannel(string name) =>
    new(s_channelManager.GetChannelByName(name));

  // TODO: Expose this as a client-managed collection.
  // public static dynamic Channels =>
  //   Unbind(s_channelManager).m_channelsByName.Keys;

  //
  // IChatManager wrapper methods
  //

  /// <summary>
  /// The internal reference to the base chat manager.
  /// </summary>
  private static IChatManager s_chatManager =>
    ObjectProvider.Get<IShellViewModel>().ChatManager;

  internal static ChatSessionViewModel GetChatForChannel(Channel channel) =>
    new(s_chatManager.GetChatForChannel(channel.@base));

  //
  // IChatManager wrapper events
  //

  /// <summary>
  /// Triggered when the user activates the chat window.
  /// </summary>
  public static EventProxy<ChatSessionEventArgs> SessionAdded =
    new(/* IChatManager */ s_chatManager, nameof(SessionAdded));

  /// <summary>
  /// Triggered when the user closes the chat window.
  /// </summary>
  public static EventProxy<ChatSessionEventArgs> SessionClosing =
    new(/* IChatManager */ s_chatManager, nameof(SessionClosing));

  public static EventProxy PassiveChatMessageReceived =
    new(/* IChatManager */ s_chatManager, nameof(PassiveChatMessageReceived));

  public static EventProxy PassiveNotificationCancelled =
    new(/* IChatManager */ s_chatManager, nameof(PassiveNotificationCancelled));
}
