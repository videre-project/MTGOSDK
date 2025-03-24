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
public class EventWrapper<T>(EventHandler handler) where T : EventArgs
{
  /// <summary>
  /// The handler method to be invoked when the event is raised.
  /// </summary>
  public void Handle(object sender, T args) => handler.Invoke(sender, args);
}
