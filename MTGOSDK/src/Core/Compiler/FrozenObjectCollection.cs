/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace MTGOSDK.Core.Compiler;

/// <summary>
/// A class that manages a collection of frozen (pinned) objects.
/// </summary>
/// <remarks>
/// This class pins objects in memory and keeps track of their addresses as
/// they are pinned. This is useful for preventing the garbage collector from
/// moving objects in memory or for preserving pointers or references to objects
/// that are passed to unmanaged code.
/// </remarks>
public class FrozenObjectsCollection
{
  private object _lock = new object();
  private Dictionary<object, ulong> _frozenObjects = new();
  private Task _freezerTask = null!;
  private ManualResetEvent _unfreezeRequested = null!;

  /// <summary>
  /// Return the address where an object is pinned, otherwise returns false.
  /// </summary>
  /// <returns>True if it was pinned, False if it wasn't</returns>
  public bool TryGetPinningAddress(object o, out ulong addr)
  {
    lock (_lock)
    {
      return _frozenObjects.TryGetValue(o, out addr);
    }
  }

  /// <summary>
  /// Pins a collection of objects.
  /// </summary>
  private void PinInternal(object[] newfrozenObjects)
  {
    lock (_lock)
    {
      if (newfrozenObjects.Length == 0)
      {
        UnpinAll();
        return;
      }

      ulong[] addresses = new ulong[newfrozenObjects.Length];
      ManualResetEvent frozenFeedback = new ManualResetEvent(false);
      ManualResetEvent unfreezeRequested = new ManualResetEvent(false);

      // Call freeze
      var func = FreezeFuncsFactory.Generate(newfrozenObjects.Length);
      Task freezerTask = Task.Run(() =>
          func(newfrozenObjects, addresses, frozenFeedback, unfreezeRequested));

      // Wait for the freezer task to signal to us
      frozenFeedback.WaitOne();

      // Dispose of last Freezer
      _unfreezeRequested?.Set();
      _freezerTask?.Wait();

      // Save new Task & event
      _unfreezeRequested = unfreezeRequested;
      _freezerTask = freezerTask;

      // Now all addresses are set in the array. Re-create dict
      _frozenObjects.Clear();
      for (int i = 0; i < newfrozenObjects.Length; i++)
      {
        _frozenObjects[newfrozenObjects[i]] = addresses[i];
      }
    }
  }

  /// <summary>
  /// Pins an object and returns the address where it is pinned.
  /// </summary>
  /// <returns>The address where the object is pinned.</returns>
  /// <remarks>
  /// If an object is already pinned, this method will simply return the address
  /// where the object is pinned.
  /// </remarks>
  public ulong Pin(object o)
  {
    lock (_lock)
    {
      // If the object is already pinned, return it's address.
      if (_frozenObjects.TryGetValue(o, out ulong addr)) return addr;

      // Prepare parameters
      object[] objs = _frozenObjects.Keys.Concat(new object[] { o }).ToArray();
      PinInternal(objs);

      return _frozenObjects[o];
    }
  }

  /// <summary>
  /// Tries to get the object that is pinned at the given address.
  /// </summary>
  /// <returns>True if the object was found, false if not.</returns>
  public bool TryGetPinnedObject(ulong addr, out object? o)
  {
    lock (_lock)
    {
      foreach (var frozenObject in _frozenObjects)
      {
        if (frozenObject.Value == addr)
        {
          o = frozenObject.Key;
          return true;
        }
      }

      o = null;
      return false;
    }
  }

  /// <summary>
  /// Unpins an object from the collection.
  /// </summary>
  /// <returns>True if it was pinned, false if not.</returns>
  public bool Unpin(ulong objAddress)
  {
    lock (_lock)
    {
      object[] objs = _frozenObjects
        .Where(kvp => kvp.Value != objAddress)
        .Select(kvp => kvp.Key)
        .ToArray();

      // Making sure that address was even in the dictionary.
      // Otherwise, we don't need to re-pin all objects.
      // Logger.Debug($"[{nameof(FrozenObjectsCollection)}] Unpinning another object. New Num Pinned: {objs.Length}");
      if (objs.Length == _frozenObjects.Count)
        return false;

      // Re-pin all objects
      PinInternal(objs);

      // Logger.Debug($"[{nameof(FrozenObjectsCollection)}] Unpinned another object. Final Num Pinned: {_frozenObjects.Count}");
      return true;
    }
  }

  /// <summary>
  /// Unpins all objects that are currently pinned.
  /// </summary>
  public void UnpinAll()
  {
    lock (_lock)
    {
      // Dispose of last Freezer
      _unfreezeRequested?.Set();
      _freezerTask?.Wait();
      _unfreezeRequested = null;
      _freezerTask = null;

      _frozenObjects.Clear();
    }
  }
}
