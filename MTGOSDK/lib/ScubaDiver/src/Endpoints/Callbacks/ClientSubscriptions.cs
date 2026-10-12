/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOSDK.Core.Logging;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  // The event subscriptions and method hooks each registration made, so a
  // client that is removed (it unregistered, or its process exited) leaves no
  // handler behind: an event handler keeps its target object alive, and a hook
  // keeps patching the method and sending callbacks.

  /// <summary>
  /// The subscriptions and hook shares connections without a registration
  /// (older clients) hold, by token: only their callbacks take the shared
  /// reference those connections receive. Guarded by <see cref="_registeredPidsLock"/>.
  /// </summary>
  private readonly Dictionary<int, int> _unregisteredSubscriptions = new();

  /// <summary>Whether a connection without a registration handles this token's callbacks.</summary>
  private bool HasUnregisteredSubscriber(int token)
  {
    lock (_registeredPidsLock) return _unregisteredSubscriptions.ContainsKey(token);
  }

  /// <summary>
  /// Records a subscription made by the current request's registration (or
  /// counts one made by a connection without a registration). One made for a
  /// registration removed meanwhile is removed at once.
  /// </summary>
  private void AddClientSubscription(int token, bool hook)
  {
    if (s_requestConnection.Value is not Guid connection) return;
    lock (_registeredPidsLock)
    {
      if (!_connectionClients.TryGetValue(connection, out var client))
      {
        _unregisteredSubscriptions.TryGetValue(token, out int shares);
        _unregisteredSubscriptions[token] = shares + 1;
        return;
      }
      if (!client.Removed)
      {
        client.Subscriptions.Add((token, hook));
        return;
      }
    }
    if (hook) RemoveHookShare(token);
    else RemoveEventSubscription(token);
  }

  private enum SubscriptionOwner { Unregistered, Owned, NotOwned }

  /// <summary>
  /// Takes one recorded subscription of the current request's registration
  /// before it is removed, so a late or repeated unsubscribe (for example after
  /// the registration's subscriptions were removed with it) never removes
  /// another client's share. A connection without a registration (an older
  /// client's, whose subscriptions are not recorded) keeps the previous
  /// behaviour.
  /// </summary>
  private SubscriptionOwner TakeClientSubscription(int token, bool hook)
  {
    if (s_requestConnection.Value is not Guid connection) return SubscriptionOwner.Unregistered;
    lock (_registeredPidsLock)
    {
      if (!_connectionClients.TryGetValue(connection, out var client))
      {
        if (_unregisteredSubscriptions.TryGetValue(token, out int shares))
        {
          if (shares > 1) _unregisteredSubscriptions[token] = shares - 1;
          else _unregisteredSubscriptions.Remove(token);
        }
        return SubscriptionOwner.Unregistered;
      }
      return client.Subscriptions.Remove((token, hook)) ? SubscriptionOwner.Owned : SubscriptionOwner.NotOwned;
    }
  }

  /// <summary>
  /// Removes every event subscription and hook share a removed registration
  /// still holds. A hook shared with other clients stays installed for them.
  /// </summary>
  private void RemoveClientSubscriptions(RegisteredClient client)
  {
    List<(int Token, bool Hook)> subscriptions;
    lock (_registeredPidsLock)
    {
      subscriptions = new(client.Subscriptions);
      client.Subscriptions.Clear();
    }

    foreach (var (token, hook) in subscriptions)
    {
      try
      {
        if (hook) RemoveHookShare(token);
        else RemoveEventSubscription(token);
      }
      catch (Exception ex)
      {
        Log.Error($"[Diver] Failed to remove subscription {token} of client {client.Pid}: {ex.Message}");
      }
    }
    if (subscriptions.Count > 0)
      Log.Information($"[Diver] Removed {subscriptions.Count} subscription(s) of client {client.Pid}.");
  }

  /// <summary>Removes an event subscription's handler; false for an unknown token.</summary>
  private bool RemoveEventSubscription(int token)
  {
    if (!_remoteEventHandler.TryRemove(token, out var registration))
      return false;
    _activeCallbacks.TryRemove(token, out _);
    lock (_registeredPidsLock) _unregisteredSubscriptions.Remove(token);
    try { RemoveEventHandler(registration.EventInfo, registration.Target, registration.RegisteredProxy); }
    finally { registration.CaptureScopeSubscription?.Dispose(); }
    return true;
  }

  /// <summary>
  /// Removes one subscription's share of a method hook, and the native patch
  /// with the last share; false for an unknown token.
  /// </summary>
  private bool RemoveHookShare(int token)
  {
    lock (_methodHookGate)
    {
      if (!_remoteHooks.TryGetValue(token, out var registration))
        return false;
      // Another subscription may still be using this shared native hook.
      if (--registration.SubscriptionCount > 0) return true;

      _remoteHooks.TryRemove(token, out _);
      _activeCallbacks.TryRemove(token, out _);
      lock (_registeredPidsLock) _unregisteredSubscriptions.Remove(token);
      HarmonyWrapper.Instance.RemoveHook(registration.OriginalHookedMethod);
      return true;
    }
  }
}
