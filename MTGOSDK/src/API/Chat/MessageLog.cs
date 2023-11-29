/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8597 // Thrown value may be null.

using System.Collections;
using System.Collections.Generic;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Chat;


namespace MTGOSDK.API.Chat;

public sealed class MessageLog(dynamic chatMessageLog)
    : DLRWrapper<IChatMessageLog>, IEnumerable<Message>, IList<Message>
{
  /// <summary>
  /// Stores an internal reference to the IChatMessageLog object.
  /// </summary>
  internal override dynamic obj => chatMessageLog;

  /// <summary>
  /// Internal reference to the message log.
  /// </summary>
  private readonly IList<Message> m_chatLog = Unbind(chatMessageLog).m_chatLog;

  //
  // IChatMessageLog wrapper properties
  //

  /// <summary>
  /// The channel's message history.
  /// </summary>
  public IEnumerable<Message> ChatHistory => Map<Message>(@base.ChatHistory);

  //
  // IList<Message> wrapper properties
  //


  /// <summary>
  /// The number of messages in the channel's message history.
  /// </summary>
  public int Count => m_chatLog.Count;

  public bool IsReadOnly => true;

  public Message this[int index] { get => m_chatLog[index]; set => throw null; }

  //
  // IList<Message> wrapper methods
  //

  public void Add(Message item) => throw null;

  public void Clear() => throw null;

  public bool Contains(Message item) => m_chatLog.Contains(item.@base);

  public void CopyTo(Message[] array, int arrayIndex) => throw null;

  public IEnumerator<Message> GetEnumerator() => ChatHistory.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

  public int IndexOf(Message item) => m_chatLog.IndexOf(item);

  public void Insert(int index, Message item) => throw null;

  public bool Remove(Message item) => throw null;

  public void RemoveAt(int index) => throw null;
}
