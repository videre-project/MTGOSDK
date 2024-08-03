/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.API.Chat;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class Chat : ChatValidationFixture
{
  [RateLimit(ms: 100)]
  [TestCase("Main Lobby")]
  [TestCase("Scheduled Events")]
  [TestCase("Constructed Queues")]
  public void Test_Channels(string name)
  {
    var channel = ChannelManager.GetChannel(name);
    ValidateChannel(channel);
    ValidateChannel(ChannelManager.GetChannel(channel.Id));
    Assert.That(ChannelManager.Channels.Any(c => c.Id == channel.Id));
  }
}

public class ChatValidationFixture : BaseFixture
{
  public void ValidateChannel(Channel channel)
  {
    // IChannel properties
    Assert.That(channel.Id,
      channel.Name == "Main Lobby"
        ? Is.EqualTo(0)
        : Is.Not.EqualTo(-1)
            // Older channels may have ids casted to the wrong type
            .And.GreaterThan(-440807));
    Assert.That(channel.Name, Is.Not.Null.Or.Empty);
    Assert.That(channel.ParentId,
      channel.Id == 0 ? Is.EqualTo(-1) : Is.GreaterThanOrEqualTo(0));
    Assert.That((bool?)channel.IsJoined, Is.Not.Null);
    Assert.That((bool?)channel.IsJoinedForGame, Is.Not.Null);
    Assert.That(channel.Messages.Take(5), Has.None.Null);
    Assert.That(channel.MessageCount, Is.GreaterThanOrEqualTo(0));
    Assert.That(channel.Users.Take(5), Has.None.Null);
    Assert.That(channel.UserCount, Is.GreaterThanOrEqualTo(0));
    Assert.That(channel.SubChannels, Has.None.Null);

    foreach(Message message in channel.Messages.Take(5))
    {
      Assert.That(message.User?.Id,
        message.User != null ? Is.GreaterThan(0) : Is.Null);
      Assert.That(message.Timestamp, Is.GreaterThan(DateTime.MinValue));
      Assert.That(message.Text, Is.Not.Null.Or.Empty);
    }

    // IChatChannel properties
    Assert.That((string?)channel.Type, Is.Not.Null);
    Assert.That((bool?)channel.CanSendMessage, Is.Not.Null);

    // IChatSessionViewModel properties
    var chatSession = channel.ChatSession;
    if (chatSession != null)
    {
      Assert.That(chatSession.Name, Is.Not.Null.Or.Empty);
      Assert.That(chatSession.Channel.Id, Is.EqualTo(channel.Id));
    }
  }
}
