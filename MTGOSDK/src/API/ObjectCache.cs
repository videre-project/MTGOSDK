/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;

using TResetter = MTGOSDK.Core.Remoting.Reflection.LazyRemoteObject.TResetter;


namespace MTGOSDK.API;

/// <summary>
/// Global cache manager for all singleton objects registered with the client.
/// </summary>
public static class ObjectCache
{
  internal static readonly object s_lock = new();
  internal static readonly ConcurrentDictionary<string, dynamic> s_instances = new();
  internal static readonly ConcurrentDictionary<string, TResetter> s_resetters = new();
  internal static readonly ConcurrentDictionary<string, Func<dynamic>> s_callbacks = new();

  /// <summary>
  /// Event raised when the ObjectProvider cache is reset.
  /// </summary>
  public static event EventHandler? OnReset;

  /// <summary>
  /// Registers a callback to reset the instance of the given type.
  /// </summary>
  /// <param name="key">The query path of the registered type.</param>
  /// <param name="callback">The callback to reset the instance.</param>
  public static void RegisterCallback(string key, Func<dynamic> callback)
  {
    if (!s_callbacks.TryAdd(key, callback))
      throw new InvalidOperationException("A duplicate callback was registered.");

    OnReset += (s, e) => Reset(key, callback);
    RemoteClient.Disposed += (s, e) => Reset(key, callback);
  }

  /// <summary>
  /// Clears the callback to reset the instance of the given type.
  /// </summary>
  /// <param name="key">The query path of the registered type.</param>
  /// <param name="callback">The callback used to reset the instance.</param>
  public static void ClearCallback(string key, Func<dynamic>? callback = null)
  {
    if (!s_callbacks.TryRemove(key, out var oldCallback))
      throw new InvalidOperationException("The callback was not registered.");

    callback ??= oldCallback;
    OnReset -= (s, e) => Reset(key, callback);
    RemoteClient.Disposed -= (s, e) => Reset(key, callback);
  }

  /// <summary>
  /// Resets the instance of the given type in the ObjectProvider cache.
  /// </summary>
  /// <param name="key">The query path of the registered type.</param>
  /// <param name="callback">The callback used to reset the instance.</param>
  public static void Reset(string key, Func<dynamic> callback)
  {
    lock (s_lock)
    {
      // Skip if no instance exists for the given key.
      if (!s_instances.TryRemove(key, out _) ||
          !s_resetters.TryGetValue(key, out var s_reset)) return;

      if (!ObjectProvider.SuppressLogging)
        Log.Trace("Resetting instance type {Type}", key);
      s_reset(callback);
      ClearCallback(key, callback);
    }
  }

  /// <summary>
  /// Triggers the OnReset event to clear objects from the cache.
  /// </summary>
  public static void Clear() => OnReset?.Invoke(null, EventArgs.Empty);
}
