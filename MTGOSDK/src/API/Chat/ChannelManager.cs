/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Interface.ViewModels;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;
using static MTGOSDK.API.Events;

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
  /// A dictionary of all channels by their channel ID.
  /// </summary>
  private static dynamic ChannelsByName =>
    Unbind(s_channelManager).m_channelsByName;

  /// <summary>
  /// All currently queryable channels in the client.
  /// </summary>
  public static IEnumerable<Channel> Channels =>
    Map<Channel>(ChannelsByName.Values);

  /// <summary>
  /// Gets the channel with the given ID.
  /// </summary>
  /// <param name="id">The ID of the channel to get.</param>
  /// <returns>A new channel object.</returns>
  public static Channel GetChannel(int id) =>
    new(Unbind(s_channelManager.GetChannelById(id)));

  /// <summary>
  /// Gets the channel with the given name.
  /// </summary>
  /// <param name="name">The name of the channel to get.</param>
  /// <returns>A new channel object.</returns>
  public static Channel GetChannel(string name) =>
    new(Unbind(s_channelManager.GetChannelByName(name)));

  //
  // IChatManager wrapper methods
  //

  /// <summary>
  /// The internal reference to the base chat manager.
  /// </summary>
  private static readonly IChatManager s_chatManager =
    Bind<IChatManager>(
      Unbind(ObjectProvider.Get<IShellViewModel>()).ChatManager);

  internal static ChatSessionViewModel? GetChatForChannel(dynamic channel) =>
    Optional<ChatSessionViewModel>(
        Unbind(s_chatManager).GetChatForChannel(channel));

  //
  // IChatManager wrapper events
  //

  public static EventProxy<ChatSessionEventArgs> SessionAdded =
    new(/* IChatManager */ s_chatManager, nameof(SessionAdded));

  public static EventProxy<ChatSessionEventArgs> SessionClosing =
    new(/* IChatManager */ s_chatManager, nameof(SessionClosing));

  public static EventProxy PassiveChatMessageReceived =
    new(/* IChatManager */ s_chatManager, nameof(PassiveChatMessageReceived));

  public static EventProxy PassiveNotificationCancelled =
    new(/* IChatManager */ s_chatManager, nameof(PassiveNotificationCancelled));
}
