/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Users;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;

public sealed class Message(dynamic chatMessage) : DLRWrapper<IChatMessage>
{
  /// <summary>
  /// Stores an internal reference to the IChatMessage object.
  /// </summary>
  internal override dynamic obj => Bind<IChatMessage>(chatMessage);

  /// <summary>
  /// Indicates whether the message has no real user object.
  /// </summary>
  private bool IsUserMessage => Try<bool>(() => chatMessage.FromUser.Id > 0);

  //
  // IChatMessage wrapper properties
  //

  /// <summary>
  /// The user who sent the message.
  /// </summary>
  public User? User => Optional<User>(IsUserMessage ? @base.FromUser : null);

  /// <summary>
  /// The timestamp of when the message was sent.
  /// </summary>
  public DateTime Timestamp => @base.Timestamp;

  /// <summary>
  /// The message content.
  /// </summary>
  public string Text => @base.Message;
}
