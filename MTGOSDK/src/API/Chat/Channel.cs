/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Users;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

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
  public ChatSessionViewModel? ChatSession =>
    field ??= ChannelManager.GetChatForChannel(chatChannel);

  //
  // IChannel wrapper properties
  //

  /// <summary>
  /// The channel's ID.
  /// </summary>
  [Default(-1)]
  public int Id => Unbind(this).Id;

  /// <summary>
  /// The channel's name.
  /// </summary>
  public string Name => Try(() => Unbind(this).Name, () => Unbind(this).Title);

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
  public IList<Message> Messages =>
    Map<IList, Message>(
      Try(() => @base.Messages,
          () => Unbind(@base.MessageLog).m_chatLog,
          () => new List<Message>()));

  /// <summary>
  /// The number of messages sent in this channel.
  /// </summary>
  public int MessageCount => Messages.Count;

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
    Try(() => Cast<ChannelType>(Unbind(this).ChannelType),
        fallback: ChannelType.System);

  /// <summary>
  /// Whether the current user can send messages to the channel.
  /// </summary>
  public bool CanSendMessage => Try<bool>(() => @base.CanSendMessage);

  //
  // ILoggableChatChannel wrapper properties
  //

  private IHistoricalChatChannel m_historicalChatChannel =>
    field ??= Bind<IHistoricalChatChannel>(Unbind(this).HistoricalChatChannel);

  /// <summary>
  /// The local file name of the chat log.
  /// </summary>
  public string LocalFileName =>
    Try(() => m_historicalChatChannel.LocalFileName,
        () => Unbind(this).LocalFileName);

  //
  // IChatChannel wrapper events
  //

  public EventProxy<ChannelEventArgs> JoinedStateChanged =
    new(/* IChatChannel */ chatChannel, nameof(JoinedStateChanged));

  public EventProxy<ChannelStateEventArgs> ChannelStateChanged =
    new(/* IChannel */ chatChannel, nameof(ChannelStateChanged));

  public EventHookWrapper<Message> OnMessageReceived =
    new(MessageReceived, new((s,_) => s.LocalFileName == chatChannel.LocalFileName));

  //
  // IChatChannel static events
  //

  /// <summary>
  /// Event triggered when a message is appended to any channel in the client.
  /// </summary>
  public static EventHookProxy<Channel, Message> MessageReceived =
    new(
      new TypeProxy<WotC.MtGO.Client.Model.Chat.LoggableChatChannel>(),
      typeof(WotC.MtGO.Client.Model.Chat.LoggableChatChannel).GetMethod(
        "SendMessage",
        [typeof(string), typeof(string)])!,
      new CapturedEventHook((context, instance, args) =>
      {
        var values = (context.Capture
          ?? throw new InvalidOperationException("Channel message capture is missing.")).Values;
        var message = new Message(new
        {
          Message = values["text"].GetScalar<string>(),
          FromUser = Optional<User>(args[1],
                                    Lambda<bool>(o => !string.IsNullOrEmpty(o))),
          Timestamp = values["timestamp"].GetScalar<DateTime>(),
          __timestamp = context.Timestamp,
        });

        return (new Channel(instance), message);
      }), CreateMessageCapture(), HarmonyPatchPosition.Prefix
    );

  private static RemoteHookCapture CreateMessageCapture()
  {
    var batch = new RemoteBatch();
    var channel = batch.Input(ObjectOrRemoteAddress.Null);
    var messageType = new RemoteBatchType(
      "WotC.MtGO.Client.Model.Chat.IChatMessage", "WotC.MtGO.Client.Model.Reference");
    var enumerableType = new RemoteBatchType("System.Collections.Generic.IEnumerable`1")
    {
      GenericArguments = [messageType]
    };
    var last = new RemoteBatchMethod(new("System.Linq.Enumerable", "System.Core"),
      "Last", enumerableType) { GenericArguments = [messageType] };
    // Every native caller appends to Messages before SendMessage. Read the last
    // item synchronously here, before another append or delayed callback delivery.
    var message = batch.CallStatic(last, batch.Read(channel, "Messages"));
    batch.Output("timestamp", batch.Read(message, "Timestamp"));
    batch.Output("text", batch.Read(message, "Message"));
    return new RemoteHookCapture { Batch = batch.ToRequest(), Inputs = [-1] };
  }

}
