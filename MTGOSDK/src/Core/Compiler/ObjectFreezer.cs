/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core.Compiler;

/// <summary>
/// A utility class that allows blittable objects to be pinned in memory.
/// </summary>
public class ObjectFreezer: IDisposable
{
  /// <summary>
  /// A delegate type for a method that pins an object and returns its address.
  /// </summary>
  public delegate IntPtr PinFunc(object obj);

  private record PinnedObjectInfo(IntPtr Address, object Data);

  private readonly ConcurrentDictionary<Type, PinFunc> _pinFuncCache = new();
  private readonly ConditionalWeakTable<object, PinnedObjectInfo> _pinnedInfoTable = new();
  private readonly ConcurrentDictionary<ulong, WeakReference<object>> _addressToObjectMap = new();

  /// <summary>
  /// Return the address where an object is pinned, otherwise returns false.
  /// </summary>
  /// <param name="o">The object to check for pinning.</param>
  /// <param name="addr">The address where the object is pinned, if successful.</param>
  /// <returns>True if it was pinned, False if it wasn't.</returns>
  public bool TryGetPinningAddress(object o, out ulong addr)
  {
    if (_pinnedInfoTable.TryGetValue(o, out var info))
    {
      addr = (ulong)info.Address.ToInt64();
      return true;
    }
    addr = 0;
    return false;
  }

  /// <summary>
  /// Tries to get the object that is pinned at the given address.
  /// </summary>
  /// <param name="addr">The address to look for a pinned object at.</param>
  /// <param name="o">The object found at the given address, if successful.</param>
  /// <returns>True if the object was found, false if not.</returns>
  public bool TryGetPinnedObject(ulong addr, out object? o)
  {
    if (_addressToObjectMap.TryGetValue(addr, out var weakRef) &&
        weakRef.TryGetTarget(out o))
    {
      return true;
    }
    o = null;
    return false;
  }

  /// <summary>
  /// Pins an object in memory and returns its address.
  /// </summary>
  /// <param name="obj">The object to pin.</param>
  /// <returns>The address of the pinned object, or IntPtr.Zero on failure.</returns>
  public IntPtr Pin(object obj)
  {
    if (_pinnedInfoTable.TryGetValue(obj, out var existingInfo))
    {
      return existingInfo.Address; // Already pinned
    }

    try
    {
      // Pin the object and return its address
      PinFunc pinFunc = _pinFuncCache.GetOrAdd(obj.GetType(), GeneratePinFunc);
      IntPtr address = pinFunc(obj);

      // Create a strong reference to prevent GC from de-allocating
      _pinnedInfoTable.Add(obj, new PinnedObjectInfo(address, obj));
      _addressToObjectMap[(ulong)address.ToInt64()] = new WeakReference<object>(obj);
      GC.KeepAlive(obj);

      return address;
    }
    catch (Exception ex)
    {
      Log.Error("Error during pinning: {0}", ex.Message);
      return IntPtr.Zero;
    }
  }

  /// <summary>
  /// Unpins an object, allowing the garbage collector to move it.
  /// </summary>
  /// <param name="obj">The object to unpin.</param>
  public void Unpin(object obj)
  {
    if (_pinnedInfoTable.TryGetValue(obj, out var info))
    {
      _pinnedInfoTable.Remove(obj);
      _addressToObjectMap.TryRemove((ulong)info.Address.ToInt64(), out _);
    }
  }

  /// <summary>
  /// Generates a dynamic method to pin an object of a specific type.
  /// </summary>
  /// <param name="type">The type of the object to pin.</param>
  /// <returns>A delegate to the generated pinning method.</returns>
  private PinFunc GeneratePinFunc(Type type)
  {
    // Create a new dynamic method.
    var pinMethod = new DynamicMethod(
      "PinInternal_" + type.Name,
      typeof(IntPtr),           // Return type is IntPtr (the address).
      new[] { typeof(object) }, // Parameter is an object (can be non-blittable).
      typeof(ObjectFreezer)     // Owner type.
    );

    // Get the IL generator for the dynamic method.
    ILGenerator il = pinMethod.GetILGenerator();

    // Declare local variables for the pinned object and its address.
    LocalBuilder pinnedLocal = il.DeclareLocal(type.MakeByRefType(), pinned: true);
    LocalBuilder addressLocal = il.DeclareLocal(typeof(IntPtr));

    // Pins the object and return its address.
    il.Emit(OpCodes.Ldarg_0);             // stack: obj
    il.Emit(OpCodes.Castclass, type);     // stack: (Type)obj
    il.Emit(OpCodes.Stloc, pinnedLocal);  // pinnedLocal = (Type)obj;
    il.Emit(OpCodes.Ldloc, pinnedLocal);  // stack: &pinnedLocal
    il.Emit(OpCodes.Conv_U);              // stack: (IntPtr)&pinnedLocal
    il.Emit(OpCodes.Stloc, addressLocal); // addressLocal = (IntPtr)&pinnedLocal;
    il.Emit(OpCodes.Ldloc, addressLocal); // stack: addressLocal
    il.Emit(OpCodes.Ret);                 // return addressLocal;

    // Creates a delegate of the PinFunc type from the generated dynamic method.
    return (PinFunc)pinMethod.CreateDelegate(typeof(PinFunc));
  }

  /// <summary>
  /// Runs a background task that periodically unpins all currently pinned objects.
  /// </summary>
  /// <param name="cancellationToken">A cancellation token to stop the task.</param>
  public void Dispose()
  {
    foreach (WeakReference<object> weakRef in _addressToObjectMap.Values)
    {
      if (weakRef.TryGetTarget(out object obj))
      {
        Unpin(obj);
      }
    }
  }
}
