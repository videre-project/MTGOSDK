/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Core.Reflection.Emit;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Utils;

using MTGOSDK.Win32.API;


namespace MTGOSDK.Core.Reflection.Snapshot;

/// <summary>
/// The snapshot runtime used to interact with the ClrMD runtime and snapshot
/// to perform runtime analysis and exploration on objects in heap memory.
/// </summary>
public class SnapshotRuntime : IDisposable
{
  internal static readonly object _clrMdLock = new();          // static
  public virtual object clrLock => SnapshotRuntime._clrMdLock; // non-static
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
  private readonly FrozenObjectsCollection _freezer = new();

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
    lock (_clrMdLock)
    {
      return _unifiedAppDomain.GetAssembly(assemblyName);
    }
  }

  public Type ResolveType(string typeFullName, string assemblyName = null)
  {
    lock (_clrMdLock)
    {
      return _unifiedAppDomain.ResolveType(typeFullName, assemblyName);
    }
  }

  public TypesDump ResolveTypes(string assemblyName)
  {
    lock (_clrMdLock)
    {
      return _unifiedAppDomain.ResolveTypes(assemblyName);
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
    _freezer.TryGetPinnedObject(objAddress, out instance);

  public ulong PinObject(object instance)
  {
    // Check if the object was pinned, otherwise ignore.
    if (!_freezer.TryGetPinningAddress(instance, out ulong objAddress))
    {
      // Pin and mark for unpinning later
      objAddress = _freezer.Pin(instance);
    }

    return objAddress;
  }

  public bool UnpinObject(ulong objAddress)
  {
    // Ignore if the object is not found in the pinned object pool.
    if (!_freezer.TryGetPinnedObject(objAddress, out _))
      return false;

    return _freezer.Unpin(objAddress);
  }

  //
  // IL.Emit runtime converter methods
  //

  public object DereferenceObject(IntPtr pObj, IntPtr expectedMethodTable) =>
    _converter.ConvertFromIntPtr(pObj, expectedMethodTable);

  public object DereferenceObject(ulong pObj, ulong expectedMethodTable) =>
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
    lock (_clrMdLock)
    {
      // NOTE: This subprocess inherits handles to DLLs in the current process
      //       so it might "lock" both the Bootstrapper and ScubaDiver dlls.
      _dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
      _runtime = _dt.ClrVersions.Single().CreateRuntime();
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
    lock (_clrMdLock)
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
    lock (_clrMdLock)
    {
      DisposeRuntime();
      CreateRuntime();
    }
  }

  public void Dispose()
  {
    DisposeRuntime();
    _freezer.UnpinAll();
  }

  //
  // ClrMD object methods
  //

  public ClrObject GetClrObject(ulong objAddr)
  {
    lock (_clrMdLock)
    {
      return _runtime.Heap.GetObject(objAddr);
    }
  }

  public ImmutableArray<ClrAppDomain> GetClrAppDomains()
  {
    lock (_clrMdLock)
    {
      return _runtime.AppDomains;
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
    ClrObject lastKnownClrObj = default;
    lock (_clrMdLock)
    {
      lastKnownClrObj = _runtime.Heap.GetObject(objAddr);
    }
    if (lastKnownClrObj == default)
    {
      throw new Exception("No object in this address. Try finding it's address again and dumping again.");
    }

    // Make sure it's still in place by refreshing the runtime
    RefreshRuntime();
    ClrObject clrObj = default;
    lock (_clrMdLock)
    {
      clrObj = _runtime.Heap.GetObject(objAddr);
    }

    //
    // Figuring out the Method Table value and the actual Object's address
    //
    ulong methodTable;
    ulong finalObjAddress;
    if (clrObj.Type != null && clrObj.Type.Name == typeName)
    {
      methodTable = clrObj.Type.MethodTable;
      finalObjAddress = clrObj.Address;
    }
    else
    {
      // Object moved; fallback to hashcode filtering (if enabled)
      if (!hashCodeFallback)
      {
        throw new Exception(
          "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
          "Hash Code fallback was NOT activated\"}");
      }

      Predicate<string> typeFilter = (string type) => type.Contains(lastKnownClrObj.Type.Name);
      (bool anyErrors, List<HeapDump.HeapObject> objects) = GetHeapObjects(typeFilter, true);
      if (anyErrors)
      {
        throw new Exception(
          "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
          "Hash Code fallback was activated but dumping function failed so non hash codes were checked\"}");
      }
      var matches = objects.Where(heapObj => heapObj.HashCode == hashcode.Value).ToList();
      if (matches.Count != 1)
      {
        throw new Exception(
          "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address. " +
          $"Hash Code fallback was activated but {((matches.Count > 1) ? "too many (>1)" : "no")} objects with the same hash code were found\"}}");
      }

      // Single match! We are as lucky as it gets :)
      HeapDump.HeapObject heapObj = matches.Single();
      ulong newObjAddress = heapObj.Address;
      finalObjAddress = newObjAddress;
      methodTable = heapObj.MethodTable;
    }

    //
    // Actually convert the address back into an Object reference.
    //
    object instance;
    try
    {
      instance = DereferenceObject(finalObjAddress, methodTable);
    }
    catch (ArgumentException)
    {
      throw new Exception("Method Table value mismatched");
    }

    //
    // A GC collect might still happen between checking the CLR MD object and
    // the retrieval of the object. So we check the final object's type name one
    // last time (it's better to crash here then return bad objects).
    //
    string finalTypeName;
    try
    {
      finalTypeName = instance.GetType().FullName;
    }
    catch (Exception ex)
    {
      throw new AggregateException(
        "The final object we got from the addres (after checking CLR MD twice) was" +
        "broken and we couldn't read it's Type's full name.", ex);
    }

    if (finalTypeName != typeName)
      throw new Exception(
        "A GC occurened between checking the CLR MD (twice) and the object retrieval." +
        "A different object was retrieved and its type is not the one we expected." +
        $"Expected Type: {typeName}, Actual Type: {finalTypeName}");


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
  public (bool anyErrors, List<HeapDump.HeapObject> objects) GetHeapObjects(
    Predicate<string> filter,
    bool dumpHashcodes)
  {
    List<HeapDump.HeapObject> objects = [];
    bool anyErrors = false;
    // Trying several times to dump all candidates
    for (int i = 0; i < 10; i++)
    {
      // Logger.Debug($"Trying to dump heap objects. Try #{i + 1}");
      // Clearing leftovers from last trial
      objects.Clear();
      anyErrors = false;

      RefreshRuntime();
      lock (_clrMdLock)
      {
        foreach (ClrObject clrObj in _runtime.Heap.EnumerateObjects())
        {
          if (clrObj.IsFree)
            continue;

          string objType = clrObj.Type?.Name ?? "Unknown";
          if (filter(objType))
          {
            ulong mt = clrObj.Type.MethodTable;
            int hashCode = 0;

            if (dumpHashcodes)
            {
              object instance = null;
              try
              {
                instance = DereferenceObject(clrObj.Address, mt);
              }
              catch (Exception)
              {
                // Exit heap enumeration and signal that the trial has failed.
                anyErrors = true;
                break;
              }

              //
              // We got the object so we haven't spotted a GC collection.
              //
              // Getting the hashcode is a challenge since objects might throw
              // exceptions on this call (e.g. System.Reflection.Emit.SignatureHelper).
              //
              // We don't REALLY care if we don't get a hash code. It just means
              // those objects would be a bit more hard to grab later.
              //
              try
              {
                hashCode = instance.GetHashCode();
              }
              catch
              {
                // TODO: Maybe we need a boolean in HeapObject to indicate we
                //       couldn't get the hashcode...
                hashCode = 0;
              }
            }

            objects.Add(new HeapDump.HeapObject()
            {
              Address = clrObj.Address,
              MethodTable = clrObj.Type.MethodTable,
              Type = objType,
              HashCode = hashCode
            });
          }
        }
      }
      if (!anyErrors)
      {
        // Success, dumped every instance there is to dump!
        break;
      }
    }
    if (anyErrors)
    {
      // Logger.Debug($"Failed to dump heap objects. Aborting.");
      objects.Clear();
    }
    return (anyErrors, objects);
  }
}
