/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using WeakReference = System.WeakReference<object>;

using MTGOSDK.Core.Compiler.Structs;
using MTGOSDK.Core.Logging;


namespace MTGOSDK.Core.Compiler;

/// <summary>
/// Pins objects in memory using a background task and IL generation,
/// preventing them from being moved by the garbage collector.
/// </summary>
public class ObjectPinner : IDisposable
{
  private record PinningInfo(IntPtr Address, uint Index);
  public readonly record struct PinRequest(object? Target, uint Index);

  /// <summary>
  /// Delegate for the pinning loop, which processes pin requests
  /// and unpin requests in a background thread.
  /// </summary>
  /// <param name="requestQueue">The queue of pin requests.</param>
  /// <param name="signal">The signal to wake up the loop.</param>
  /// <param name="shouldExit">Flag to indicate if the loop should exit.</param>
  public delegate void PinningLoopDelegate(
    ConcurrentQueue<PinRequest> requestQueue,
    ManualResetEventSlim signal,
    ref bool shouldExit);

  private bool _shouldExit = false;
  private readonly ReaderWriterLockSlim _lock = new();
  private readonly Task _pinningTask;
  private readonly uint _size;

  private readonly ConcurrentQueue<PinRequest> _requestQueue = new();
  private readonly ManualResetEventSlim _signal = new(false);

  private readonly Stack<uint> _freeIndices = new();
  private uint _nextIndex = 0;
  private readonly ConditionalWeakTable<object, PinningInfo> _weakTable = new();
  private readonly ConcurrentDictionary<IntPtr, WeakReference> _addrMap = new();

  public ObjectPinner(uint size = 1024)
  {
    if (size == 0) throw new ArgumentOutOfRangeException(nameof(size), "Size must be greater than zero.");
    _size = size;

    // Create the dynamic method for the pinning loop
    DynamicMethod method = GeneratePinningLoopIL(_size);
    var loopDelegate = (PinningLoopDelegate)method.CreateDelegate(typeof(PinningLoopDelegate));

    // Start the background task
    _pinningTask = Task.Factory.StartNew(
      () =>
      {
        try
        {
          loopDelegate(_requestQueue, _signal, ref _shouldExit);
        }
        catch (Exception ex)
        {
          Log.Error("Exception in ObjectPinner task", ex);
          Log.Debug(ex.Message + "\n" + ex.StackTrace);
        }
      },
      CancellationToken.None,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default);
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (disposing)
    {
      if (!_shouldExit)
      {
        _shouldExit = true;
        _signal?.Set(); // Wake up the task if it's waiting

        try
        {
          // Wait for the task to complete with a timeout
          if (_pinningTask != null && !_pinningTask.IsCompleted)
          {
            _pinningTask.Wait(TimeSpan.FromSeconds(3));
          }
        }
        catch (AggregateException ex)
        {
          Log.Warning("ObjectPinner task did not shut down gracefully", ex);
        }
        catch (ObjectDisposedException) { /* Signal may already be disposed */ }
        finally
        {
          _pinningTask?.Dispose(); // Dispose task if possible
        }
      }

      _lock?.Dispose();
      _signal?.Dispose();
    }
  }

  public bool TryGetPinningAddress(object obj, out IntPtr objAddress)
  {
    if (obj == null) throw new ArgumentNullException(nameof(obj));

    _lock.EnterReadLock();
    try
    {
      if (_weakTable.TryGetValue(obj, out PinningInfo? pinningInfo))
      {
        objAddress = pinningInfo.Address;
        return true;
      }
    }
    finally
    {
      _lock.ExitReadLock();
    }

    objAddress = IntPtr.Zero;
    return false;
  }

  public bool TryGetPinnedObject(IntPtr objAddress, out object? obj)
  {
    if (objAddress == IntPtr.Zero)
      throw new ArgumentNullException(nameof(objAddress));

    _lock.EnterReadLock();
    try
    {
      if (_addrMap.TryGetValue(objAddress, out WeakReference? weakRef) &&
        weakRef.TryGetTarget(out obj) && obj != null)
      {
        return true;
      }
    }
    finally
    {
      _lock.ExitReadLock();
    }

    obj = null;
    return false;
  }

  public IntPtr Pin(object obj)
  {
    if (obj == null) throw new ArgumentNullException(nameof(obj));

    if (!TryPinObject(obj, out IntPtr objAddr))
    {
       throw new InvalidOperationException(
          "Failed to pin object. No free slots available or internal error.");
    }
    Log.Trace($"Pinned object {obj} at address {objAddr}.");
    Log.Trace($"Now {_addrMap.Count} objects pinned.");
    return objAddr;
  }

  public void Unpin(object obj)
  {
    if (obj == null) throw new ArgumentNullException(nameof(obj));

    _lock.EnterWriteLock();
    try
    {
      if (!_weakTable.TryGetValue(obj, out PinningInfo? pinningInfo))
      {
        return; // Object not pinned, do nothing
      }

      _weakTable.Remove(obj);
      _addrMap.TryRemove(pinningInfo.Address, out _);

      // Enqueue unpin request (null object signifies unpin for that index)
      _requestQueue.Enqueue(new PinRequest(null, pinningInfo.Index));
      _signal.Set(); // Signal background task

      // Return index to free list
      _freeIndices.Push(pinningInfo.Index);
      Log.Trace($"Unpinned object {obj} at address {pinningInfo.Address}.");
      Log.Trace($"Now {_addrMap.Count} objects pinned.");
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Unpins all objects and clears all dictionaries and mappings
  /// from the ObjectPinner.
  /// </summary>
  public void UnpinAllObjects()
  {
    _lock.EnterWriteLock();
    try
    {
      // Clear all weak references and free indices
      _freeIndices.Clear();
      _nextIndex = 0;

      // Clear all mappings and reset state
      foreach (var kvp in _addrMap)
      {
        var weakRef = kvp.Value;
        if (weakRef.TryGetTarget(out object? obj) && obj != null)
        {
          // Remove the entry from the weak table
          if (_weakTable.TryGetValue(obj, out PinningInfo? pinningInfo))
          {
            // Enqueue unpin request (null object signifies unpin for that index)
            _requestQueue.Enqueue(new PinRequest(null, pinningInfo.Index));
            _weakTable.Remove(obj);
          }
        }
      }
      _addrMap.Clear();
      _signal.Set(); // Signal background task
      Log.Trace("Unpinned all objects.");
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  private unsafe bool TryPinObject(object obj, out IntPtr objAddr)
  {
    _lock.EnterUpgradeableReadLock();
    try
    {
      // Check if already pinned
      if (_weakTable.TryGetValue(obj, out PinningInfo? pinningInfo))
      {
        objAddr = pinningInfo.Address;
        return true;
      }

      // Acquire write lock to modify state
      _lock.EnterWriteLock();
      try
      {
        // Get next available index
        uint index;
        if (_freeIndices.Count > 0)
        {
          index = _freeIndices.Pop();
        }
        else if (_nextIndex < _size)
        {
          index = _nextIndex++;
        }
        else
        {
          // No more slots available
          objAddr = IntPtr.Zero;
          return false;
        }

        // Temporarily pin the object to compute the address
        var b = Unsafe.As<Pinnable>(obj);
        fixed (byte* c = &b.Data)
        {
          objAddr = (IntPtr)(c - IntPtr.Size);
        }

        // Add to mappings before signaling background task
        if (_addrMap.TryAdd(objAddr, new WeakReference(obj)))
        {
          _weakTable.Add(obj, new PinningInfo(objAddr, index));

          // Enqueue request for background task after maps are updated
          _requestQueue.Enqueue(new PinRequest(obj, index));
          _signal.Set(); // Signal background task

          return true;
        }
        else
        {
          // Failed to add to addrMap (potentially due to an address collision)
          _freeIndices.Push(index); // Rollback index
          objAddr = IntPtr.Zero;
          return false;
        }
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

  private static DynamicMethod GeneratePinningLoopIL(uint size)
  {
    var method = new DynamicMethod(
      "PinningLoop_" + Guid.NewGuid().ToString("N"),
      null, // Return type void
      new[] {
        typeof(ConcurrentQueue<PinRequest>), // Arg 0: requestQueue
        typeof(ManualResetEventSlim),        // Arg 1: signal
        typeof(bool).MakeByRefType()         // Arg 2: ref shouldExit
      },
      typeof(ObjectPinner).Module,
      true // skipVisibility
    );

    ILGenerator il = method.GetILGenerator();

    // --- Get MethodInfo for Unsafe.As ---
    MethodInfo unsafeAsMethod = typeof(Unsafe)
      .GetMethod("As", new Type[] { typeof(object) })
        ?? throw new MissingMethodException("Cannot find Unsafe.As<T>(object)");
    MethodInfo unsafeAsPinnable = unsafeAsMethod.MakeGenericMethod(typeof(Pinnable));

    // --- Get FieldInfo for Pinnable.Data ---
    FieldInfo pinnableDataField = typeof(Pinnable).GetField("Data")
      ?? throw new MissingFieldException("Cannot find Pinnable.Data field");

    // --- Locals ---
    LocalBuilder[] pinnedLocals = new LocalBuilder[size];
    for (int i = 0; i < size; i++)
    {
      pinnedLocals[i] = il.DeclareLocal(typeof(byte).MakeByRefType(), pinned: true);
      // Initialize pinned locals to null
      il.Emit(OpCodes.Ldnull);
      il.Emit(OpCodes.Stloc, pinnedLocals[i]);
    }
    LocalBuilder requestLocal = il.DeclareLocal(typeof(PinRequest));
    LocalBuilder objLocal = il.DeclareLocal(typeof(object));
    LocalBuilder indexLocal = il.DeclareLocal(typeof(uint));

    // --- Labels ---
    Label loopStart = il.DefineLabel();
    Label checkExit = il.DefineLabel();
    Label processQueueLoop = il.DefineLabel();
    Label processQueueEnd = il.DefineLabel();
    Label handlePin = il.DefineLabel();
    Label handleUnpin = il.DefineLabel();
    Label storeResult = il.DefineLabel();
    Label storeCompleteLabel = il.DefineLabel();
    Label loopExit = il.DefineLabel();

    // --- Method Body ---
    il.MarkLabel(loopStart);

    // Wait for signal
    il.Emit(OpCodes.Ldarg_1);
    il.Emit(OpCodes.Callvirt,
      typeof(ManualResetEventSlim).GetMethod("Wait", Type.EmptyTypes));

    // Check exit flag
    il.MarkLabel(checkExit);
    il.Emit(OpCodes.Ldarg_2);
    il.Emit(OpCodes.Ldind_I1);
    il.Emit(OpCodes.Brtrue, loopExit);

    // Process Queue Loop
    il.MarkLabel(processQueueLoop);
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Ldloca, requestLocal);
    il.Emit(OpCodes.Callvirt,
      typeof(ConcurrentQueue<PinRequest>).GetMethod("TryDequeue"));
    il.Emit(OpCodes.Brfalse, processQueueEnd);

    // Extract info
    il.Emit(OpCodes.Ldloca, requestLocal);
    il.Emit(OpCodes.Call,
      typeof(PinRequest).GetProperty("Target").GetGetMethod());
    il.Emit(OpCodes.Stloc, objLocal);

    il.Emit(OpCodes.Ldloca, requestLocal);
    il.Emit(OpCodes.Call,
      typeof(PinRequest).GetProperty("Index").GetGetMethod());
    il.Emit(OpCodes.Stloc, indexLocal);

    // Check if object is null
    il.Emit(OpCodes.Ldloc, objLocal);
    il.Emit(OpCodes.Brfalse, handleUnpin);

    // --- HandlePin ---
    il.MarkLabel(handlePin);
    il.Emit(OpCodes.Ldloc, objLocal);
    il.Emit(OpCodes.Call, unsafeAsPinnable);
    il.Emit(OpCodes.Ldflda, pinnableDataField);
    il.Emit(OpCodes.Br, storeResult);

    // --- HandleUnpin ---
    il.MarkLabel(handleUnpin);
    il.Emit(OpCodes.Ldnull);
    // Fall through to storeResult

    // Store result (managed ref or null) into pinned local
    il.MarkLabel(storeResult);

    // Use switch based on indexLocal
    Label[] indexCaseLabels = new Label[size];
    for (int i = 0; i < size; i++)
    {
      indexCaseLabels[i] = il.DefineLabel();
    }

    il.Emit(OpCodes.Ldloc, indexLocal);
    il.Emit(OpCodes.Switch, indexCaseLabels);

    // Default case for switch (index out of range - no check done here)
    // If index is invalid, this pops the value and jumps past storage.
    il.Emit(OpCodes.Pop);
    il.Emit(OpCodes.Br, storeCompleteLabel);

    // Emit the code block for each index case
    for (int i = 0; i < size; i++)
    {
      il.MarkLabel(indexCaseLabels[i]);
      il.Emit(OpCodes.Stloc, pinnedLocals[i]);
      il.Emit(OpCodes.Br, storeCompleteLabel);
    }

    // Common exit point for all cases
    il.MarkLabel(storeCompleteLabel);

    il.Emit(OpCodes.Br, processQueueLoop);

    // --- End of Queue Processing ---
    il.MarkLabel(processQueueEnd);

    il.Emit(OpCodes.Br, loopStart);

    // --- Exit Point ---
    il.MarkLabel(loopExit);
    il.Emit(OpCodes.Ret);

    return method;
  }
}
