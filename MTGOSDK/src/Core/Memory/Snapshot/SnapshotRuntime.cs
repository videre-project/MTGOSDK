/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8500

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Core.Compiler;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
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
  /// Token counter for generating unique object identifiers.
  /// </summary>
  private static long _nextToken = 0;

  /// <summary>
  /// Maps tokens to pinned objects (logical pinning - strong references keep objects alive).
  /// </summary>
  private readonly ConcurrentDictionary<ulong, object> _pinnedObjects = new();

  /// <summary>
  /// Reverse lookup from object to its token.
  /// </summary>
  private readonly ConditionalWeakTable<object, BoxedToken> _objectToToken = new();

  private record BoxedToken(ulong Token);

  /// <summary>
  /// The number of objects currently pinned (held by strong references).
  /// </summary>
  public int PinnedObjectCount => _pinnedObjects.Count;

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
    // UnifiedAppDomain.ResolveType uses a ConcurrentDictionary cache internally,
    // so we don't need the read lock for the common cached path.
    // Assembly enumeration is thread-safe and the cache handles concurrent access.
    return _unifiedAppDomain.ResolveType(typeFullName, assemblyName);
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
  // Token-based object tracking (logical pinning)
  //

  public bool TryGetPinnedObject(ulong token, out object instance)
  {
    bool found = _pinnedObjects.TryGetValue(token, out instance);
    Log.Debug($"[SnapshotRuntime] TryGetPinnedObject(token={token}) => found={found}, poolSize={_pinnedObjects.Count}");
    return found;
  }

  public ulong PinObject(object instance)
  {
    // ConditionalWeakTable.GetValue is thread-safe and guarantees the factory
    // is called exactly once per key, making this lock-free and correct.
    var boxedToken = _objectToToken.GetValue(instance, obj =>
    {
      var token = (ulong) Interlocked.Increment(ref _nextToken);
      _pinnedObjects[token] = obj;
      Log.Debug($"[SnapshotRuntime] PinObject: NEW token={token} for type={obj?.GetType().Name}, poolSize={_pinnedObjects.Count}");
      return new BoxedToken(token);
    });
    Log.Debug($"[SnapshotRuntime] PinObject: returning token={boxedToken.Token}");
    return boxedToken.Token;
  }

  public bool UnpinObject(ulong token)
  {
    Log.Debug($"[SnapshotRuntime] UnpinObject(token={token})");
    if (!_pinnedObjects.TryRemove(token, out var obj))
    {
      Log.Debug($"[SnapshotRuntime] UnpinObject: token={token} NOT FOUND in pool");
      return false;
    }

    _objectToToken.Remove(obj);
    Log.Debug($"[SnapshotRuntime] UnpinObject: token={token} REMOVED, poolSize={_pinnedObjects.Count}");
    return true;
  }

  /// <summary>
  /// Queue a non-blocking unpin request by token. With dictionary-based tracking,
  /// this is equivalent to immediate unpinning.
  /// </summary>
  public void QueueUnpinObject(ulong token) => UnpinObject(token);

  public void UnpinAllObjects() => _pinnedObjects.Clear();

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
      _dt = DataTarget.AttachToProcess(Process.GetCurrentProcess().Id, suspend: false);
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

      _dt?.Dispose();
      _dt = null;
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
    _pinnedObjects.Clear();

    // NOTE: _lock is static and shared across all instances, so we do NOT dispose it.
    // Disposing a static lock would break subsequent SnapshotRuntime instances.
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
        IntPtr ptr = *(IntPtr*) (&tr);

        return (ulong) ptr.ToInt64();
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
      Log.Debug($"[SnapshotRuntime] Object 0x{objAddr:X} found in pinned pool");
      return (pinnedObj, objAddr);
    }
    Log.Debug($"[SnapshotRuntime] Object 0x{objAddr:X} NOT in pinned pool, falling back to ClrMD lookup");

    //
    // The object is not pinned, so falling back to the last dumped runtime can
    // help ensure we can find the object by it's type information if it moves.
    //
    ClrObject lastKnownClrObj = GetClrObject(objAddr);
    if (lastKnownClrObj == default)
    {
      throw new Exception("No object in this address.");
    }

    ClrObject clrObj = lastKnownClrObj;
    if (clrObj.Type?.Name != typeName)
    {
      Log.Debug($"[SnapshotRuntime] RefreshRuntime() starting for object 0x{objAddr:X}...");
      var sw = Stopwatch.StartNew();
      RefreshRuntime();
      Log.Debug($"[SnapshotRuntime] RefreshRuntime() completed in {sw.ElapsedMilliseconds}ms");

      clrObj = GetClrObject(objAddr);
    }

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

      // Ensure we have type information before searching
      if (lastKnownClrObj.Type == null)
      {
        throw new RemoteObjectMovedException(objAddr,
          $"Object 0x{objAddr:X} has no type information. " +
          "This may indicate the object has been garbage collected or is corrupted.");
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

  // =========================================================================
  // Heap Analysis (snapshot + retain path + instance listing)
  // =========================================================================

  // Cached analysis state from the last TakeHeapSnapshot call
  private static HeapSnapshotResponse _cachedSnapshot;
  private static Dictionary<string, List<TypeInstance>> _cachedInstances;

  /// <summary>
  /// Walks the managed heap to collect per-type statistics and cache instance
  /// lists. This is the fast path — no reference walking.
  /// </summary>
  /// <summary>
  /// Bounded insertion: keeps only the top N largest instances per type
  /// without accumulating all instances in memory.
  /// </summary>
  private static void InsertBounded(
    Dictionary<string, List<TypeInstance>> dict,
    string typeName,
    TypeInstance inst,
    int maxPerType = 20)
  {
    if (!dict.TryGetValue(typeName, out var list))
    {
      list = new List<TypeInstance>(maxPerType + 1);
      dict[typeName] = list;
    }

    // If under capacity, just add
    if (list.Count < maxPerType)
    {
      list.Add(inst);
      return;
    }

    // Find the smallest in the list; replace if new is larger
    int minIdx = 0;
    long minSize = list[0].Size;
    for (int i = 1; i < list.Count; i++)
    {
      if (list[i].Size < minSize)
      {
        minSize = list[i].Size;
        minIdx = i;
      }
    }
    if (inst.Size > minSize)
      list[minIdx] = inst;
  }

  public HeapSnapshotResponse TakeHeapSnapshot(int topN = 50)
  {
    var typeAgg = new Dictionary<string, TypeStats>();
    var instancesByType = new Dictionary<string, List<TypeInstance>>();
    long totalSize = 0;
    long totalCount = 0;

    _lock.EnterWriteLock();
    try
    {
      RefreshRuntime();

      using var gcContext = GCTimer.SuppressGC();
      try
      {
        _lock.ExitWriteLock();
        _lock.EnterReadLock();

        var heap = _runtime.Heap;

        foreach (ClrObject obj in heap.EnumerateObjects())
        {
          if (obj.IsFree || obj.Type == null || obj.Type.Name == null)
            continue;

          string typeName = obj.Type.Name;
          long size = (long)obj.Size;
          totalSize += size;
          totalCount++;

          // Determine generation
          int gen = -1;
          var seg = heap.GetSegmentByAddress(obj.Address);
          if (seg != null)
            gen = (int)seg.GetGeneration(obj.Address);

          // Aggregate per-type stats
          if (!typeAgg.TryGetValue(typeName, out var stats))
          {
            stats = new TypeStats { TypeName = typeName };
            typeAgg[typeName] = stats;
          }
          stats.Count++;
          stats.TotalSize += size;
          switch (gen)
          {
            case 0: stats.Gen0Count++; stats.Gen0Size += size; break;
            case 1: stats.Gen1Count++; stats.Gen1Size += size; break;
            case 2: stats.Gen2Count++; stats.Gen2Size += size; break;
            case >= 3: stats.LohCount++; stats.LohSize += size; break;
          }

          // Track top 20 largest instances per type (bounded, no bulk alloc)
          InsertBounded(instancesByType, typeName, new TypeInstance
          {
            Address = obj.Address,
            Size = size,
            Generation = gen
          });
        }
      }
      finally
      {
        if (_lock.IsReadLockHeld)
          _lock.ExitReadLock();
        if (!_lock.IsWriteLockHeld)
          _lock.EnterWriteLock();
      }
    }
    finally
    {
      if (_lock.IsWriteLockHeld)
        _lock.ExitWriteLock();
    }

    // Sort the bounded instance lists by size desc
    foreach (var kvp in instancesByType)
      kvp.Value.Sort((a, b) => b.Size.CompareTo(a.Size));

    var sortedTypes = typeAgg.Values
      .OrderByDescending(t => t.TotalSize)
      .Take(topN)
      .ToList();

    var response = new HeapSnapshotResponse
    {
      Types = sortedTypes,
      TotalHeapSize = totalSize,
      TotalObjectCount = totalCount,
    };

    // Cache for subsequent queries
    _cachedSnapshot = response;
    _cachedInstances = instancesByType;

    return response;
  }

  /// <summary>
  /// Returns cached instances of the specified type from the last snapshot.
  /// </summary>
  public TypeInstancesResponse GetTypeInstances(string typeName, int maxCount = 20)
  {
    var instances = _cachedInstances;
    if (instances == null || !instances.TryGetValue(typeName, out var list))
      return new TypeInstancesResponse { Instances = new List<TypeInstance>() };

    return new TypeInstancesResponse
    {
      Instances = list.Take(maxCount).ToList()
    };
  }

  //
  // Retain chain: batched reverse BFS (on-demand, per-type)
  //

  /// <summary>
  /// Field cache keyed by method table — avoids re-reflecting the same type.
  /// Cleared on RefreshRuntime.
  /// </summary>
  private static readonly Dictionary<ulong, ClrInstanceField[]> s_fieldCache = new();

  /// <summary>
  /// Returns the cached array of object-reference fields for the given type.
  /// </summary>
  private static ClrInstanceField[] ResolveFieldsCached(ClrType type)
  {
    var mt = type.MethodTable;
    if (!s_fieldCache.TryGetValue(mt, out var fields))
    {
      fields = type.Fields
        .Where(f => f.IsObjectReference)
        .ToArray();
      s_fieldCache[mt] = fields;
    }
    return fields;
  }

  /// <summary>
  /// Computes the retain chain for the largest cached instance of the given
  /// type using a batched reverse BFS. Each depth level does ONE heap scan
  /// that resolves referrers for all current target addresses simultaneously.
  /// </summary>
  public RetainChainResponse GetRetainChain(string typeName, int maxDepth = 8)
  {
    if (_cachedInstances == null ||
        !_cachedInstances.TryGetValue(typeName, out var insts) ||
        insts.Count == 0)
    {
      return new RetainChainResponse
      {
        Chain = new List<RetainPathEntry>(),
        SampleAddress = 0
      };
    }

    ulong sampleAddr = (ulong)insts[0].Address;
    Log.Debug("[HeapAnalysis] GetRetainChain: type={Type}, sampleAddr={Addr:X}",
      typeName, sampleAddr);

    // parentMap: child address → (parent address, field name, parent type, parent size)
    var parentMap = new Dictionary<ulong, (ulong Parent, string Field, string Type, long Size)>();
    var currentTargets = new HashSet<ulong> { sampleAddr };

    s_fieldCache.Clear();

    // Single snapshot for the entire BFS — refreshing between depth levels
    // invalidates addresses from prior levels, causing empty chains.
    _lock.EnterWriteLock();
    try
    {
      RefreshRuntime();
      using var gcContext = GCTimer.SuppressGC();
      try
      {
        _lock.ExitWriteLock();
        _lock.EnterReadLock();

        var heap = _runtime.Heap;

        for (int depth = 0; depth < maxDepth && currentTargets.Count > 0; depth++)
        {
          var nextTargets = new HashSet<ulong>();
          int remaining = currentTargets.Count;

          foreach (ClrObject obj in heap.EnumerateObjects())
          {
            if (remaining <= 0) break;
            if (obj.IsFree || obj.Type == null || obj.Type.Name == null)
              continue;

            try
            {
              // Try named fields first for field name resolution
              string matchedField = null;
              ulong matchedTarget = 0;
              var fields = ResolveFieldsCached(obj.Type);
              foreach (var field in fields)
              {
                try
                {
                  var refAddr = field.ReadObject(obj.Address, interior: false).Address;
                  if (refAddr != 0 && currentTargets.Contains(refAddr) &&
                      !parentMap.ContainsKey(refAddr))
                  {
                    matchedField = field.Name;
                    matchedTarget = refAddr;
                    break;
                  }
                }
                catch { }
              }

              // If no named field matched, check via EnumerateReferences
              // (covers array elements, base class fields, struct refs, etc.)
              if (matchedTarget == 0)
              {
                foreach (var refObj in obj.EnumerateReferences(
                  carefully: true, considerDependantHandles: true))
                {
                  if (currentTargets.Contains(refObj.Address) &&
                      !parentMap.ContainsKey(refObj.Address))
                  {
                    matchedTarget = refObj.Address;
                    break;
                  }
                }
              }

              if (matchedTarget != 0)
              {
                parentMap[matchedTarget] = (
                  obj.Address, matchedField, obj.Type.Name, (long)obj.Size);
                nextTargets.Add(obj.Address);
                remaining--;
              }
            }
            catch { }
          }

          // Only trace addresses we haven't already traced
          currentTargets = nextTargets;
          currentTargets.ExceptWith(parentMap.Keys);
          Log.Debug("[HeapAnalysis] Depth {Depth}: found {Found} parents, {Next} next targets, parentMap size={Map}",
            depth, nextTargets.Count, currentTargets.Count, parentMap.Count);
        }
      }
      finally
      {
        if (_lock.IsReadLockHeld)
          _lock.ExitReadLock();
        if (!_lock.IsWriteLockHeld)
          _lock.EnterWriteLock();
      }
    }
    finally
    {
      if (_lock.IsWriteLockHeld)
        _lock.ExitWriteLock();
    }

    // Assemble chain: walk from sample address → root via parentMap
    Log.Debug("[HeapAnalysis] Assembling chain from {Addr:X}, parentMap has {Count} entries",
      sampleAddr, parentMap.Count);
    var chain = new List<RetainPathEntry>();
    ulong addr = sampleAddr;
    var visited = new HashSet<ulong>();
    while (parentMap.TryGetValue(addr, out var p) && visited.Add(addr))
    {
      Log.Debug("[HeapAnalysis]   {Addr:X} -> {Parent:X} ({Type}.{Field})",
        addr, p.Parent, p.Type, p.Field ?? "(null)");
      chain.Add(new RetainPathEntry
      {
        Address = p.Parent,
        TypeName = p.Type,
        Size = p.Size,
        FieldName = p.Field,
      });
      addr = p.Parent;
    }
    chain.Reverse();
    Log.Debug("[HeapAnalysis] Final chain length: {Len}", chain.Count);

    return new RetainChainResponse
    {
      Chain = chain,
      SampleAddress = sampleAddr
    };
  }

  //
  // Static holders: who owns the most retained memory via static fields
  //

  /// <summary>
  /// Enumerates every static object reference in the process and reports
  /// each one's direct size plus the sizes of its immediate children (one
  /// hop of EnumerateReferences). This gives visibility past singleton
  /// shells without requiring a full heap graph walk.
  /// </summary>
  /// <remarks>
  /// Cost: O(static fields × avg fanout). Typically under 1 second for
  /// ~2000 static roots with ~50 avg children each. No graph algorithm,
  /// no attribution heuristics — every number is a direct field read.
  /// </remarks>
  public StaticHoldersResponse AnalyzeStaticHolders(int topN = 50)
  {
    var holders = new List<StaticHolder>();

    _lock.EnterWriteLock();
    try
    {
      RefreshRuntime();
      using var gcContext = GCTimer.SuppressGC();
      try
      {
        _lock.ExitWriteLock();
        _lock.EnterReadLock();

        var heap = _runtime.Heap;

        const long MaxSingleObjectBytes = 2L * 1024 * 1024 * 1024;

        long GetSafeSize(ClrObject obj)
        {
          if (obj.Type == null) return 0;
          long baseSize = obj.Type.StaticSize;
          long reported = (long)obj.Size;
          if (reported <= 0) return baseSize > 0 ? baseSize : 0;

          var seg = heap.GetSegmentByAddress(obj.Address);
          if (seg == null) return 0;
          long segRemainder = (long)(seg.End - obj.Address);
          long cap = Math.Min(segRemainder, MaxSingleObjectBytes);

          if (reported > cap)
            return baseSize > 0 && baseSize <= cap ? baseSize : 0;
          return reported;
        }

        // ---- Step 1: collect static roots ----
        var staticRoots =
          new List<(string HolderType, string FieldName, ClrObject Target)>();
        var seenMethodTables = new HashSet<ulong>();

        foreach (var module in _runtime.EnumerateModules())
        {
          IEnumerable<(ulong MethodTable, int Token)> typeDefs;
          try { typeDefs = module.EnumerateTypeDefToMethodTableMap(); }
          catch { continue; }

          foreach (var (mt, _) in typeDefs)
          {
            if (mt == 0 || !seenMethodTables.Add(mt)) continue;

            ClrType type;
            try { type = _runtime.GetTypeByMethodTable(mt); }
            catch { continue; }
            if (type?.Name == null) continue;

            ImmutableArray<ClrStaticField> statics;
            try { statics = type.StaticFields; }
            catch { continue; }
            if (statics.IsDefault) continue;

            foreach (var field in statics)
            {
              if (field == null || !field.IsObjectReference) continue;
              foreach (var domain in _runtime.AppDomains)
              {
                try
                {
                  var obj = field.ReadObject(domain);
                  if (obj.IsValid && !obj.IsNull && obj.Address != 0 &&
                      heap.GetSegmentByAddress(obj.Address) != null)
                    staticRoots.Add((type.Name, field.Name, obj));
                }
                catch { }
              }
            }
          }
        }

        Log.Information("[HeapAnalysis] AnalyzeStaticHolders: {Count} static roots",
          staticRoots.Count);

        // Stable ordering for reproducible results across snapshots.
        staticRoots.Sort((a, b) =>
          string.CompareOrdinal(
            a.HolderType + "." + a.FieldName,
            b.HolderType + "." + b.FieldName));

        // ---- Step 2: for each root, compute direct + one-hop sizes ----
        //
        // "Direct" = the size of the object the static field points at.
        // "One-hop" = sizes of all objects the root target directly
        //             references (via EnumerateReferences on the target
        //             only — NOT recursive). This sees past singleton
        //             shells into their instance-field collections.
        //
        // RetainedBytes = directSize + sum(childSizes)
        // ObjectCount   = 1 + number of direct children
        // DominantChild = the single largest child object

        var seen = new HashSet<ulong>(); // de-dup children across roots
        foreach (var (holderType, fieldName, rootObj) in staticRoots)
        {
          long directSize = GetSafeSize(rootObj);
          if (directSize <= 0) continue;

          long childrenSize = 0;
          int childCount = 0;
          string bestChildType = null;
          long bestChildSize = 0;

          try
          {
            foreach (var child in rootObj.EnumerateReferences(
              carefully: true, considerDependantHandles: false))
            {
              if (child.Address == 0) continue;
              if (heap.GetSegmentByAddress(child.Address) == null) continue;

              long cs = GetSafeSize(child);
              if (cs <= 0) continue;

              childrenSize += cs;
              childCount++;

              if (cs > bestChildSize)
              {
                bestChildSize = cs;
                bestChildType = child.Type?.Name ?? "?";
              }
            }
          }
          catch { }

          holders.Add(new StaticHolder
          {
            HolderType = holderType,
            FieldName = fieldName,
            RootAddress = rootObj.Address,
            RootTypeName = rootObj.Type?.Name ?? "?",
            RetainedBytes = directSize + childrenSize,
            ObjectCount = 1 + childCount,
            DominantChildType = bestChildType,
            DominantChildSize = bestChildSize,
          });
        }
      }
      finally
      {
        if (_lock.IsReadLockHeld) _lock.ExitReadLock();
        if (!_lock.IsWriteLockHeld) _lock.EnterWriteLock();
      }
    }
    finally
    {
      if (_lock.IsWriteLockHeld) _lock.ExitWriteLock();
    }

    long totalRetained = 0;
    foreach (var h in holders) totalRetained += h.RetainedBytes;

    Log.Information("[HeapAnalysis] AnalyzeStaticHolders: {Count} holders, {Bytes} total retained",
      holders.Count, totalRetained);

    return new StaticHoldersResponse
    {
      Holders = holders
        .OrderByDescending(h => h.RetainedBytes)
        .Take(topN)
        .ToList(),
      TotalStaticRoots = holders.Count,
      TotalRetainedBytes = totalRetained,
    };
  }
}

