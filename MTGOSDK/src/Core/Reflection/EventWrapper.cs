/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Wraps a generic method to be used as an event handler for subscription.
/// </summary>
/// <typeparam name="T">The type of the event arguments.</typeparam>
/// <param name="handler">The method to be wrapped.</param>
public class EventWrapper<T> where T : EventArgs
{
  private readonly EventHandler _handler;
  private readonly object _source;
  private readonly string _eventName;

  public EventWrapper(EventHandler handler, object source = null, string eventName = null)
  {
    _handler = handler;
    _source = source;
    _eventName = eventName;
  }

  /// <summary>
  /// The handler method to be invoked when the event is raised.
  /// </summary>
  public void Handle(object sender, T args)
  {
    Action callback = () => _handler.Invoke(sender, args);

    // Generate a meaningful group ID for this event callback
    string groupId = GenerateGroupId(sender, args);

    // Enqueue with the group ID for proper sequencing
    SyncThread.Enqueue(callback, groupId);
  }

  /// <summary>
  /// Generates a consistent group ID for related event callbacks.
  /// </summary>
  private string GenerateGroupId(object sender, T args)
  {
    // Use the original source object if available (from constructor)
    object source = _source ?? sender;

    // Create group ID based on:
    // 1. Source object type (where the event comes from)
    // 2. Event name (if provided)
    // 3. Event args type
    string sourceType = source?.GetType().FullName ?? "UnknownSource";
    string eventType = typeof(T).FullName;

    if (!string.IsNullOrEmpty(_eventName))
      return $"Event:{sourceType}.{_eventName}:{eventType}";

    // If no event name was specified, use the args type name to identify the event
    return $"Event:{sourceType}:{eventType}";
  }
}
