/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8500

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Compiler;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Core.Memory.Snapshot;

/// <summary>
/// The snapshot runtime used to interact with the ClrMD runtime and snapshot
/// to perform runtime analysis and exploration on objects in heap memory.
/// </summary>
public class SnapshotRuntime : IDisposable
{
  private static readonly ReaderWriterLockSlim _lock =
    new(LockRecursionPolicy.SupportsRecursion);
  public virtual object clrLock => _lock;

  // Internal ClrMD runtime and data target objects to manage the snapshot.
  private DataTarget _dt;
  private ClrRuntime _runtime;

  /// <summary>
  /// The unified application domain object used to resolve type reflection.
  /// </summary>
  private readonly UnifiedAppDomain _unifiedAppDomain = new();

  /// <summary>
  /// The converter used to convert an object address to an object instance.
  /// </summary>
  private readonly Converter<object> _converter = new();

  /// <summary>
  /// The collection of frozen (pinned) objects.
  /// </summary>
  private readonly ObjectPinner _pinner = new();

  public SnapshotRuntime(bool useDomainSearch = false)
  {
    this.CreateRuntime();
    _unifiedAppDomain = new UnifiedAppDomain(useDomainSearch ? this : null);
  }

  //
  // UnifiedAppDomain wrapper methods
  //

  public Assembly ResolveAssembly(string assemblyName)
  {
    _lock.EnterReadLock();
    try
    {
      return _unifiedAppDomain.GetAssembly(assemblyName);
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  public Type ResolveType(string typeFullName, string assemblyName = null)
  {
    _lock.EnterReadLock();
    try
    {
      return _unifiedAppDomain.ResolveType(typeFullName, assemblyName);
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  public TypesDump ResolveTypes(string assemblyName)
  {
    _lock.EnterReadLock();
    try
    {
      return _unifiedAppDomain.ResolveTypes(assemblyName);
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  /// <summary>
  /// Returns an object based on an encoded object for a remote object address.
  /// </summary>
  /// <param name="param">The object or remote address.</param>
  /// <returns>The object instance.</returns>
  public object ParseParameterObject(ObjectOrRemoteAddress param)
  {
    switch (param)
    {
      case { IsNull: true }:
        return null;
      case { IsType: true }:
        return ResolveType(param.Type, param.Assembly);
      case { IsRemoteAddress: false }:
        return PrimitivesEncoder.Decode(param.EncodedObject, param.Type);
      case { IsRemoteAddress: true }:
        if (TryGetPinnedObject(param.RemoteAddress, out object pinnedObj))
        {
          return pinnedObj;
        }
        break;
    }

    throw new NotImplementedException(
      $"Unable to parse the given address into an object of type `{param.Type}`");
  }

  //
  // PinnedObjectCollection wrapper methods
  //

  public bool TryGetPinnedObject(ulong objAddress, out object instance) =>
    _pinner.TryGetPinnedObject((IntPtr)objAddress, out instance);

  public ulong PinObject(object instance)
  {
    // Check if the object was pinned, otherwise ignore.
    if (!_pinner.TryGetPinningAddress(instance, out IntPtr objAddress))
    {
      // Pin and mark for unpinning later
      objAddress = _pinner.Pin(instance);
    }

    return (ulong)objAddress;
  }

  public bool UnpinObject(ulong objAddress)
  {
    // Ignore if the object is not found in the pinned object pool.
    if (!_pinner.TryGetPinnedObject((IntPtr)objAddress, out object obj))
      return false;

    _pinner.Unpin(obj);
    return true;
  }

  public void UnpinAllObjects() => _pinner.UnpinAllObjects();

  //
  // IL.Emit runtime converter methods
  //

  public object Compile(IntPtr pObj, IntPtr expectedMethodTable) =>
    _converter.ConvertFromIntPtr(pObj, expectedMethodTable);

  public object Compile(ulong pObj, ulong expectedMethodTable) =>
    _converter.ConvertFromIntPtr(pObj, expectedMethodTable);

  //
  // ClrMD runtime and data target lifecycle methods
  //

  /// <summary>
  /// Creates the ClrMD runtime and data target (snapshot process).
  /// </summary>
  /// <remarks>
  /// This works like 'fork()': it creates a secondary snapshot process which is
  /// a copy of the current one, but without any running threads. Then our
  /// process attaches to the snapshot process and reads its memory.
  /// <para>
  /// This method is thread-safe and should be called before any runtime access.
  /// </para>
  /// </remarks>
  public void CreateRuntime()
  {
    _lock.EnterWriteLock();
    try
    {
      // NOTE: This subprocess inherits handles to DLLs in the current process
      //       so it might "lock" both the Bootstrapper and ScubaDiver dlls.
      _dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
      _runtime = _dt.ClrVersions.Single().CreateRuntime();
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Disposes the ClrMD runtime and data target (snapshot process).
  /// </summary>
  /// <remarks>
  /// This method is thread-safe and can be called even if no runtime exists.
  /// </remarks>
  public void DisposeRuntime()
  {
    _lock.EnterWriteLock();
    try
    {
      _runtime?.Dispose();
      _runtime = null;

      // Check the DataReader's state as a proxy to the snapshot process state.
      var dr = _dt?.DataReader;
      if (dr == null) return;

      //
      // We use reflection to control the disposal of the snapshot process from
      // the WindowsProcessDataReader class. This is because the 'Dispose()'
      // method called by the DataTarget class attempts to kill the process
      // without waiting for process exit and without the correct privileges.
      //
      // As this a result, a Win32Exception is thrown which will repeatedly spam
      // any subscribed Trace listeners (as a Trace.Write is called internally).
      //
      // Refer to:
      // https://github.com/microsoft/clrmd/pull/926
      // https://github.com/microsoft/clrmd/blob/f1b5dd2aed90e46d3b1d7a44b1d86dba5336dac0/src/Microsoft.Diagnostics.Runtime/DataReaders/Windows/WindowsProcessDataReader.cs#L76-L113
      //
      try
      {
        if (dr.GetType().Name == "WindowsProcessDataReader")
        {
          // Parse snapshot process ID from DataReader display name
          var id = int.Parse(dr.DisplayName["pid:".Length..],
              System.Globalization.NumberStyles.HexNumber);

          // Kill the snapshot process
          var proc = Process.GetCurrentProcess();
          var snapshotProc = Process.GetProcessById(id);
          if (snapshotProc.ProcessName == proc.ProcessName)
          {
            try
            {
              // Get the snapshot process handle
              var _snapshotHandle = (IntPtr)dr.GetType()
                .GetField("_snapshotHandle",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(dr);
              // Free snapshot memory
              if (Kernel32.PssFreeSnapshot(proc.Handle, _snapshotHandle) != 0)
              {
                throw new Win32Exception("Failed to free snapshot memory");
              }
            }
            finally
            {
              snapshotProc.Kill();
              snapshotProc.WaitForExit();
            }
          }

          // Close the native process handle
          var nativeHandle = (IntPtr)dr.GetType()
            .GetField("_process",
                BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(dr);
          Kernel32.CloseHandle(nativeHandle);

          // Mark DataReader as disposed
          dr.GetType()
            .GetField("_disposed",
                BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(dr, true);
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine("Failed to dispose DataReader: " + ex.ToString());
      }
      finally
      {
        _dt?.Dispose();
        _dt = null;
      }
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Refreshes the ClrMD runtime and data target (snapshot process).
  /// </summary>
  /// <remarks>
  /// Refer to the <see cref="CreateRuntime"/> and <see cref="DisposeRuntime"/>
  /// methods for more information on the snapshot process lifecycle.
  /// <para>
  /// This method is thread-safe and should be called to refresh the runtime
  /// after any changes to the target process or runtime state.
  /// </para>
  /// </remarks>
  public void RefreshRuntime()
  {
    _lock.EnterWriteLock();
    try
    {
      DisposeRuntime();
      CreateRuntime();
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  public void Dispose()
  {
    DisposeRuntime();
    _pinner.Dispose();

    if (_lock != null)
    {
      _lock.Dispose();
    }
  }

  //
  // ClrMD object methods
  //

  public ulong GetObjectAddress(object obj)
  {
    _lock.EnterReadLock();
    try
    {
      unsafe
      {
        TypedReference tr = __makeref(obj);
        IntPtr ptr = *(IntPtr*)(&tr);

        return (ulong)ptr.ToInt64();
      }
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  public ClrObject GetClrObject(ulong objAddr)
  {
    _lock.EnterReadLock();
    try
    {
      return _runtime.Heap.GetObject(objAddr);
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  public ImmutableArray<ClrAppDomain> GetClrAppDomains()
  {
    _lock.EnterReadLock();
    try
    {
      return _runtime.AppDomains;
    }
    finally
    {
      _lock.ExitReadLock();
    }
  }

  /// <summary>
  /// Gets an object from the heap based on its address and type name.
  /// </summary>
  /// <param name="objAddr">The object address.</param>
  /// <param name="pinningRequested">Whether to pin the object.</param>
  /// <param name="typeName">The expected type name.</param>
  /// <param name="hashcode">The object hash code.</param>
  /// <returns>The object instance and pinned address.</returns>
  /// <exception cref="Exception">Thrown when the object is not found.</exception>
  /// <remarks>
  /// This method is used to retrieve an object from the heap based on its
  /// location in the ClrMD snapshot. It will attempt to resolve the object by
  /// its type name and hash code, and will fallback to searching by using the
  /// last known object type if the object has moved.
  /// <para>
  /// Any objects that have been pinned will be returned directly from the
  /// frozen objects collection. Otherwise, the object will be resolved from the
  /// snapshot and converted to an object instance.
  /// </para>
  /// </remarks>
  public (object instance, ulong pinnedAddress) GetHeapObject(
    ulong objAddr,
    bool pinningRequested,
    string typeName,
    int? hashcode = null)
  {
    bool hashCodeFallback = hashcode.HasValue;

    // Check if we have this object in our pinned pool
    if (TryGetPinnedObject(objAddr, out object pinnedObj))
    {
      return (pinnedObj, objAddr);
    }

    //
    // The object is not pinned, so falling back to the last dumped runtime can
    // help ensure we can find the object by it's type information if it moves.
    //
    ClrObject lastKnownClrObj = GetClrObject(objAddr);
    if (lastKnownClrObj == default)
    {
      throw new Exception("No object in this address.");
    }

    // Make sure it's still in place by refreshing the runtime
    RefreshRuntime();
    ClrObject clrObj = GetClrObject(objAddr);

    //
    // Figuring out the Method Table value and the actual Object's address
    //
    object instance = null;
    if (clrObj.Type != null && clrObj.Type.Name == typeName)
    {
      instance = Compile(clrObj.Address, clrObj.Type.MethodTable);
    }
    else
    {
      // Object moved; fallback to hashcode filtering (if enabled)
      if (!hashCodeFallback)
      {
        throw new RemoteObjectMovedException(objAddr,
          $"Object moved since last refresh. Object 0x{objAddr:X} " +
          "could not be found in the heap.");
      }

      // Directly search the heap for the moved object by type and hashcode
      bool found = false;
      string expectedTypeName = lastKnownClrObj.Type.Name;
      _lock.EnterReadLock(); // Need read lock for heap enumeration
      try
      {
        foreach (ClrObject currentObj in _runtime.Heap.EnumerateObjects())
        {
          if (currentObj.IsFree || currentObj.Type == null ||
              !currentObj.Type.Name.Contains(expectedTypeName))
            continue;

          try
          {
            // Compile and check hashcode
            instance = Compile(currentObj.Address, currentObj.Type.MethodTable);
            if (instance != null && instance.GetHashCode() == hashcode.Value)
            {
              found = true;
              break; // Found the object, stop searching
            }
          }
          catch
          {
            // Ignore errors during compilation or GetHashCode for this specific
            // object, continue searching
          }
        }
      }
      finally
      {
        if (_lock.IsReadLockHeld)
          _lock.ExitReadLock();
      }

      if (!found)
      {
        throw new RemoteObjectMovedException(objAddr,
          $"Object moved since last refresh. Object 0x{objAddr:X} " +
          "could not be found in the heap, and no object matching the type " +
          $"'{expectedTypeName}' was found with the hash code {hashcode.Value}.");
      }
    }

    // Pin the result object if requested
    ulong pinnedAddress = 0;
    if (pinningRequested)
    {
      pinnedAddress = PinObject(instance);
    }

    return (instance, pinnedAddress);
  }

  /// <summary>
  /// Gets all heap objects that match the given type filter.
  /// </summary>
  /// <param name="filter">The type filter predicate.</param>
  /// <param name="dumpHashcodes">Whether to dump object hash codes.</param>
  /// <returns>The list of heap objects.</returns>
  /// <remarks>
  /// This method is used to dump all heap objects that match the given type
  /// filter. It will attempt to enumerate all objects in the heap and filter
  /// them by their type name. If the 'dumpHashcodes' parameter is set, it will
  /// also attempt to retrieve the hash code of each object.
  /// <para>
  /// This method will attempt to dump all objects several times to ensure that
  /// all objects are captured. If any errors occur during the enumeration, the
  /// method will return an empty list of objects and set 'anyErrors' to true.
  /// </para>
  /// </remarks>
  public List<HeapDump.HeapObject> GetHeapObjects(
    Predicate<string> filter,
    bool dumpHashcodes)
  {
    List<HeapDump.HeapObject> objects = [];
    bool anyErrors = false;

    // Take a write lock since we'll modify runtime state
    _lock.EnterWriteLock();
    try
    {
      // Refresh runtime while holding write lock
      RefreshRuntime();

      // Suspend GC while enumerating objects
      using var gcContext = GCTimer.SuppressGC();
      try
      {
        // Now downgrade to read lock for enumeration
        _lock.ExitWriteLock();
        _lock.EnterReadLock();

        foreach (ClrObject clrObj in _runtime.Heap.EnumerateObjects())
        {
          if (clrObj.IsFree || clrObj.Type == null)
            continue;

          string objType = clrObj.Type.Name;
          if (!filter(objType))
            continue;

          ulong mt = clrObj.Type.MethodTable;
          int hashCode = 0;

          if (dumpHashcodes)
          {
            try
            {
              // Get instance and hash code atomically
              object instance = Compile(clrObj.Address, mt);
              if (instance != null)
              {
                try { hashCode = instance.GetHashCode(); }
                catch { /* Ignore hash code errors */ }
              }
            }
            catch (Exception)
            {
              anyErrors = true;
              continue;
            }
          }

          objects.Add(new HeapDump.HeapObject()
          {
            Address = clrObj.Address,
            MethodTable = mt,
            Type = objType,
            HashCode = hashCode
          });
        }
      }
      finally
      {
        // Release the read lock we acquired after downgrading
        if (_lock.IsReadLockHeld)
          _lock.ExitReadLock();

        // Reacquire write lock if we downgraded
        if (!_lock.IsWriteLockHeld)
          _lock.EnterWriteLock();
      }
    }
    finally
    {
      // Always release the write lock
      if (_lock.IsWriteLockHeld)
        _lock.ExitWriteLock();
    }

    if (anyErrors && objects.Count == 0)
      throw new HeapDumpException(
        "Failed to dump heap objects. No objects were found and errors occurred.");

    return objects;
  }
}
