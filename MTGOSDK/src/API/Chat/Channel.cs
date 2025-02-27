/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Users;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;
using static MTGOSDK.API.Events;

/// <summary>
/// A wrapper for the MTGO client's <see cref="IChatChannel"/> interface.
/// </summary>
public sealed class Channel(dynamic chatChannel)
    : DLRWrapper<IChatChannel>
{
  /// <summary>
  /// Stores an internal reference to the IChatChannel object.
  /// </summary>
  internal override dynamic obj => Bind<IChatChannel>(chatChannel);

  private dynamic ChatLog =>
    Try(() => Unbind(@base.MessageLog).m_chatLog,
        () => new List<Message>());

  /// <summary>
  /// The ChatSessionViewModel of the channel.
  /// </summary>
  /// <remarks>
  /// This is an instance of the chat's session view model, which is used by the
  /// client to control the client-side management of chat state and UI elements.
  /// </remarks>
  public ChatSessionViewModel? ChatSession =>
    ChannelManager.GetChatForChannel(chatChannel);

  //
  // IChannel wrapper properties
  //

  /// <summary>
  /// The channel's ID.
  /// </summary>
  [Default(-1)]
  public int Id => @base.Id;

  /// <summary>
  /// The channel's name.
  /// </summary>
  public string Name => @base.Name;

  /// <summary>
  /// The parent channel's ID.
  /// </summary>
  public int ParentId => @base.ParentId;

  /// <summary>
  /// Whether the user is joined to this channel.
  /// </summary>
  public bool IsJoined => @base.IsJoined;

  /// <summary>
  /// Whether the user is joined to this channel for the current game.
  /// </summary>
  public bool IsJoinedForGame => @base.IsJoinedForGameSubscription;

  /// <summary>
  /// The log of messages in this channel.
  /// </summary>
  public IList<Message> Messages => Map<IList, Message>(ChatLog, proxy: true);

  /// <summary>
  /// The number of messages sent in this channel.
  /// </summary>
  public int MessageCount => ChatLog.Count;

  /// <summary>
  /// The users in this channel.
  /// </summary>
  public IEnumerable<User> Users => Map<User>(@base.Users);

  /// <summary>
  /// The number of users in this channel.
  /// </summary>
  public int UserCount => @base.UserCount;

  /// <summary>
  /// The sub-channels parented to this channel.
  /// </summary>
  public IEnumerable<Channel> SubChannels => Map<Channel>(@base.SubChannels);

  //
  // IChatChannel wrapper properties
  //

  /// <summary>
  /// The type of chat channel (e.g. "System", "GameChat", "GameLog", etc.)
  /// </summary>
  public ChannelType Type =>
    Try(() => Cast<ChannelType>(Unbind(@base).ChannelType),
        fallback: ChannelType.System);

  /// <summary>
  /// Whether the current user can send messages to the channel.
  /// </summary>
  public bool CanSendMessage => @base.CanSendMessage;

  //
  // ILoggableChatChannel wrapper properties
  //

  private IHistoricalChatChannel m_historicalChatChannel =>
    Bind<IHistoricalChatChannel>(Unbind(@base).HistoricalChatChannel);

  /// <summary>
  /// The local file name of the chat log.
  /// </summary>
  public string LocalFileName => m_historicalChatChannel.LocalFileName;

  //
  // IChatChannel wrapper events
  //

  public EventProxy<ChannelEventArgs> JoinedStateChanged =
    new(/* IChatChannel */ chatChannel, nameof(JoinedStateChanged));

  public EventProxy<ChannelStateEventArgs> ChannelStateChanged =
    new(/* IChannel */ chatChannel, nameof(ChannelStateChanged));
}
