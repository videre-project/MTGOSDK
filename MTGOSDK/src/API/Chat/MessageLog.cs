/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;

public sealed class MessageLog(dynamic chatMessageLog)
    : DLRWrapper<IChatMessageLog>
{
  /// <summary>
  /// Stores an internal reference to the IChatMessageLog object.
  /// </summary>
  internal override dynamic obj => chatMessageLog;

  //
  // IChatMessageLog wrapper properties
  //

  /// <summary>
  /// The channel's message history.
  /// </summary>
  public IEnumerable<Message> ChatHistory =>
    Map<Message>(@base.ChatHistory);

  //
  // IChatMessageLog wrapper methods
  //

  /// <summary>
  /// Adds a message to the chat log.
  /// </summary>
  /// <param name="args">The message to add.</param>
  public void Add(Message args) =>
    @base.Add(/* IChatMessage */ args.@base);

  /// <summary>
  /// Clears the chat log.
  /// </summary>
  public void Clear() =>
    @base.Clear();
}
