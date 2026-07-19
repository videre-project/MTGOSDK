/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.API.Interface.ViewModels;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Shiny.Core.Interfaces;
using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;
using static MTGOSDK.API.Events;

public static class ChannelManager
{
  /// <summary>
  /// The internal reference to the model chat service.
  /// </summary>
  private static readonly IChat s_chat = ObjectProvider.Get<IChat>();

  //
  // IChannelManager wrapper methods
  //

  internal static readonly ConcurrentDictionary<int, IHistoricalChatChannel>
    s_gameLogChannels = new();

  /// <summary>
  /// Manages the client's set information and card definitions.
  /// </summary>
  private static readonly IChannelManager s_channelManager =
    ObjectProvider.Get<IChannelManager>();

  static ChannelManager()
  {
    ObjectCache.OnReset += delegate { ChannelsByName = null; };
  }

  /// <summary>
  /// A dictionary of all channels by their channel ID.
  /// </summary>
  private static dynamic ChannelsByName
  {
    get => field ??= Unbind(s_channelManager).m_channelsByName;
    set => field = value;
  }

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

  /// <summary>
  /// Gets or creates the private chat channel for the given user.
  /// </summary>
  /// <param name="userId">The ID of the other user.</param>
  /// <returns>The private channel.</returns>
  public static Channel GetPrivateChannel(int userId) =>
    new(Unbind(s_chat.GetPrivateChatChannel(userId)));

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
