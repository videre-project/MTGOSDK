/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Reflection;

using Shiny.Chat;
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

  /// <summary>
  /// The ChatSessionViewModel of the channel.
  /// </summary>
  /// <remarks>
  /// This is an instance of the chat's session view model, which is used by the
  /// client to control the client-side management of chat state and UI elements.
  /// </remarks>
  public ChatSessionViewModel ChatSession =>
    new(ChannelManager.GetChatForChannel(this));

  //
  // IChannel wrapper properties
  //

  /// <summary>
  /// The channel's ID.
  /// </summary>
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
  public MessageLog Log => new(@base.MessageLog);

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
  public IEnumerable<Channel> SubChannels =>
    Map<Channel>(Unbind(@base).SubChannels);

  //
  // IChatChannel wrapper properties
  //

  /// <summary>
  /// The name of the chat channel.
  /// </summary>
  public string Title => @base.Title;

  /// <summary>
  /// The type of chat channel (e.g. "System", "Private", "GameChat", "GameLog")
  /// </summary>
  public string Type => Unbind(@base).ChannelType.ToString();

  /// <summary>
  /// Whether the current user can send messages to the channel.
  /// </summary>
  public bool CanSendMessage => @base.CanSendMessage;

  /// <summary>
  /// The messages sent in the channel.
  /// </summary>
  public IEnumerable<Message> Messages =>
    Map<Message>(Unbind(@base).Messages);

  //
  // IChatChannel wrapper events
  //

  public EventProxy<ChannelEventArgs> JoinedStateChanged =
    new(/* IChatChannel */ chatChannel);

  public EventProxy<ChannelStateEventArgs> ChannelStateChanged =
    new(/* IChannel */ chatChannel);
}
