/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Buffers;

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
public class FrozenObjectCollection
{
  private readonly ReaderWriterLockSlim _lock =
    new(LockRecursionPolicy.SupportsRecursion);

  private readonly Dictionary<object, ulong> _frozenObjects = new();
  private readonly ArrayPool<object> _arrayPool = ArrayPool<object>.Shared;
  private readonly ArrayPool<ulong> _addressPool = ArrayPool<ulong>.Shared;

  private Task _freezerTask = null!;
  private ManualResetEvent _unfreezeRequested = null!;

  /// <summary>
  /// Return the address where an object is pinned, otherwise returns false.
  /// </summary>
  /// <returns>True if it was pinned, False if it wasn't</returns>
  public bool TryGetPinningAddress(object o, out ulong addr)
  {
    _lock.EnterReadLock();
    try
    {
      return _frozenObjects.TryGetValue(o, out addr);
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  /// <summary>
  /// Pins a collection of objects.
  /// </summary>
  private void PinInternal(object[] newfrozenObjects)
  {
    _lock.EnterWriteLock();
    try
    {
      if (newfrozenObjects.Length == 0)
      {
        UnpinAll();
        return;
      }

      ulong[] addresses = _addressPool.Rent(newfrozenObjects.Length);
      try
      {
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
      finally
      {
        _addressPool.Return(addresses);
      }
    }
    finally
    {
      _lock.ExitWriteLock();
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
    _lock.EnterUpgradeableReadLock();
    try
    {
      // If the object is already pinned, return it's address.
      if (_frozenObjects.TryGetValue(o, out ulong addr)) return addr;

      _lock.EnterWriteLock();
      try
      {
        // Rent array from pool
        object[] objs = _arrayPool.Rent(_frozenObjects.Count + 1);
        try
        {
          _frozenObjects.Keys.CopyTo(objs, 0);
          objs[_frozenObjects.Count] = o;
          PinInternal(objs.AsSpan(0, _frozenObjects.Count + 1).ToArray());
        }
        finally
        {
          _arrayPool.Return(objs);
        }

        return _frozenObjects[o];
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }
    finally
    {
      _lock.ExitUpgradeableReadLock();
    }
  }

  /// <summary>
  /// Tries to get the object that is pinned at the given address.
  /// </summary>
  /// <returns>True if the object was found, false if not.</returns>
  public bool TryGetPinnedObject(ulong addr, out object? o)
  {
    _lock.EnterReadLock();
    try
    {
      foreach (var kvp in _frozenObjects)
      {
        if (kvp.Value == addr)
        {
          o = kvp.Key;
          return true;
        }
      }
      o = null;
      return false;
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  /// <summary>
  /// Unpins an object from the collection.
  /// </summary>
  /// <returns>True if it was pinned, false if not.</returns>
  public bool Unpin(ulong objAddress)
  {
    _lock.EnterWriteLock();
    try
    {
      // Rent array from pool
      object[] objs = _arrayPool.Rent(_frozenObjects.Count);
      try
      {
        int count = 0;
        foreach (var kvp in _frozenObjects)
        {
          if (kvp.Value != objAddress)
          {
            objs[count++] = kvp.Key;
          }
        }

        // If no object was removed, return false
        if (count == _frozenObjects.Count)
        {
          return false;
        }

        // Re-pin remaining objects
        PinInternal(objs.AsSpan(0, count).ToArray());
        return true;
      }
      finally
      {
        _arrayPool.Return(objs);
      }
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Unpins all objects that are currently pinned.
  /// </summary>
  public void UnpinAll()
  {
    _lock.EnterWriteLock();
    try
    {
      // Dispose of last Freezer
      _unfreezeRequested?.Set();
      _freezerTask?.Wait();
      _unfreezeRequested = null;
      _freezerTask = null;

      _frozenObjects.Clear();
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }
}
