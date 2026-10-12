/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Structs;
using MTGOSDK.Core.Remoting.Types;
using MTGOSDK.Resources;


namespace MTGOSDK.Core.Remoting;

public class RemoteHandle : DLRWrapper, IDisposable
{
  public sealed class RemoteObjectCacheDiagnostics
  {
    public int RemoteObjectEntries { get; set; }
    public int RemoteObjectLiveEntries { get; set; }
    public int RemoteObjectStaleEntries { get; set; }
    public int RemoteObjectRefEntries { get; set; }
    public int RemoteObjectRefLiveEntries { get; set; }
    public int RemoteObjectRefStaleEntries { get; set; }
  }

  internal class RemoteObjectsCollection
  {
    // Lock-free cache using ConcurrentDictionary for better concurrent performance.
    // Store the weak reference directly: a Lazy factory closes over the newly
    // created RemoteObject and keeps it strongly rooted until the cache entry is
    // removed.
    private readonly ConcurrentDictionary<ulong, WeakReference<RemoteObject>> _pinnedAddressesToRemoteObjects;

    // Cache for RemoteObjectRef to ensure refs are shared across RemoteObject recreations
    // This prevents multiple refs to the same Diver token from unpinning prematurely
    private readonly ConcurrentDictionary<ulong, WeakReference<RemoteObjectRef>> _pinnedAddressesToRefs;
    private const int RemoteObjectRefLockStripes = 64;
    private readonly object[] _remoteObjectRefLocks = CreateRemoteObjectRefLockStripes();

    private const int CacheSweepInterval = 256;
    private int _cacheSweepCounter;

    private readonly RemoteHandle _app;

    public RemoteObjectsCollection(RemoteHandle app)
    {
      _app = app;
      _pinnedAddressesToRemoteObjects = new ConcurrentDictionary<ulong, WeakReference<RemoteObject>>();
      _pinnedAddressesToRefs = new ConcurrentDictionary<ulong, WeakReference<RemoteObjectRef>>();
    }

    private void SweepStaleCacheEntries(bool force = false)
    {
      if (!force &&
          Interlocked.Increment(ref _cacheSweepCounter) % CacheSweepInterval != 0)
        return;

      foreach (var entry in _pinnedAddressesToRemoteObjects)
      {
        if (!entry.Value.TryGetTarget(out var remoteObject) ||
            !remoteObject.IsValid)
        {
          _pinnedAddressesToRemoteObjects.TryRemove(entry.Key, out _);
        }
      }

      foreach (var entry in _pinnedAddressesToRefs)
      {
        if (!entry.Value.TryGetTarget(out var remoteObjectRef) ||
            !remoteObjectRef.IsValid)
        {
          _pinnedAddressesToRefs.TryRemove(entry.Key, out _);
        }
      }
    }

    public RemoteObjectCacheDiagnostics GetDiagnostics()
    {
      SweepStaleCacheEntries(force: true);

      var diagnostics = new RemoteObjectCacheDiagnostics
      {
        RemoteObjectEntries = _pinnedAddressesToRemoteObjects.Count,
        RemoteObjectRefEntries = _pinnedAddressesToRefs.Count,
      };

      foreach (var entry in _pinnedAddressesToRemoteObjects)
      {
        if (entry.Value.TryGetTarget(out var remoteObject) &&
            remoteObject.IsValid)
        {
          diagnostics.RemoteObjectLiveEntries++;
        }
        else
        {
          diagnostics.RemoteObjectStaleEntries++;
        }
      }

      foreach (var entry in _pinnedAddressesToRefs)
      {
        if (entry.Value.TryGetTarget(out var remoteObjectRef) &&
            remoteObjectRef.IsValid)
        {
          diagnostics.RemoteObjectRefLiveEntries++;
        }
        else
        {
          diagnostics.RemoteObjectRefStaleEntries++;
        }
      }

      return diagnostics;
    }

    public void Clear()
    {
      _pinnedAddressesToRemoteObjects.Clear();
      _pinnedAddressesToRefs.Clear();
    }

    private static object[] CreateRemoteObjectRefLockStripes()
    {
      var locks = new object[RemoteObjectRefLockStripes];
      for (int i = 0; i < locks.Length; i++)
      {
        locks[i] = new object();
      }
      return locks;
    }

    private object GetRemoteObjectRefLock(ulong pinnedAddress) =>
      _remoteObjectRefLocks[(int) (pinnedAddress % (ulong) _remoteObjectRefLocks.Length)];

    private bool TryGetLiveRemoteObjectRef(
      ulong pinnedAddress,
      out RemoteObjectRef remoteObjectRef)
    {
      remoteObjectRef = null!;
      if (_pinnedAddressesToRefs.TryGetValue(pinnedAddress, out var weakRef) &&
          weakRef.TryGetTarget(out var existingRef) &&
          existingRef.IsValid)
      {
        remoteObjectRef = existingRef;
        return true;
      }

      return false;
    }

    // Pin accounting. The Diver adds one reference to an object's pin each time
    // it returns the object, and one /unpin removes one. The client keeps one
    // RemoteObjectRef per token holding one of those references, unpinned when
    // its last RemoteObject is released. Every other reference a receipt brings
    // (PinReceipt) is settled once the receipt has a holder on a live object:
    //  - Surplus: a response (or a callback argument, pinned once for every
    //    client it was sent to) pinned the object for this client again; the
    //    extra reference is returned at once (otherwise the object stays pinned
    //    for as long as the client is attached);
    //  - None: no reference was taken (heap lookups), or a new ref adopted it.
    internal enum PinReceipt { None, Surplus }

    private void SettleReceipt(ulong pinnedAddress, ref PinReceipt receipt)
    {
      var pending = receipt;
      receipt = PinReceipt.None;
      if (pending == PinReceipt.None) return;
      // Returned in the background: the caller's ref still holds its own
      // reference, so the object stays pinned for as long as it is held.
      _app.Communicator.ReleaseLater(pinnedAddress);
    }

    private RemoteObject PublishRemoteObjectRef(
      ulong pinnedAddress,
      Func<RemoteObjectRef> createRef,
      ref PinReceipt receipt)
    {
      lock (GetRemoteObjectRefLock(pinnedAddress))
      {
        if (TryGetLiveRemoteObjectRef(pinnedAddress, out var existingRef))
        {
          try
          {
            var shared = new RemoteObject(existingRef, _app);
            SettleReceipt(pinnedAddress, ref receipt);
            return shared;
          }
          catch (ObjectDisposedException)
          {
            // Released meanwhile: a new ref adopts this receipt's reference.
          }
        }

        RemoteObjectRef objRef = createRef();
        _pinnedAddressesToRefs[pinnedAddress] = new WeakReference<RemoteObjectRef>(objRef);
        SweepStaleCacheEntries();

        receipt = PinReceipt.None; // the new ref holds this receipt's reference
        return new RemoteObject(objRef, _app);
      }
    }

    /// <summary>Takes a holder on the cached, live RemoteObject for an address.</summary>
    private bool TryAcquireCached(ulong address, out RemoteObject remoteObject)
    {
      remoteObject = null!;
      if (!_pinnedAddressesToRemoteObjects.TryGetValue(address, out var weakRef))
        return false;
      if (weakRef.TryGetTarget(out var ro) && ro.TryAddReference())
      {
        if (ro.IsValid)
        {
          remoteObject = ro;
          return true;
        }
        ro.ReleaseReference();
      }
      // Stale entry (only removed if it is still this one).
      ((ICollection<KeyValuePair<ulong, WeakReference<RemoteObject>>>) _pinnedAddressesToRemoteObjects)
        .Remove(new KeyValuePair<ulong, WeakReference<RemoteObject>>(address, weakRef));
      return false;
    }

    /// <summary>
    /// Caches a newly built RemoteObject, or hands out the one cached meanwhile.
    /// The new object keeps its holder (and so its pin) until another live
    /// holder has been acquired, so the receipt is never left without a pin.
    /// </summary>
    private RemoteObject PublishToCache(ulong address, RemoteObject newRo)
    {
      while (true)
      {
        if (TryAcquireCached(address, out var cached))
        {
          newRo.ReleaseReference();
          return cached;
        }
        if (_pinnedAddressesToRemoteObjects.TryAdd(address, new WeakReference<RemoteObject>(newRo)))
        {
          return newRo;
        }
      }
    }

    private RemoteObject GetRemoteObjectUncached(
      ulong remoteAddress,
      string typeName,
      int? hashCode = null)
    {
      if (TryGetLiveRemoteObjectRef(remoteAddress, out var existingRef))
      {
        try
        {
          return new RemoteObject(existingRef, _app); // no reference taken
        }
        catch (ObjectDisposedException)
        {
          // Released meanwhile: dump and pin below.
        }
      }

      ObjectDump od = null!;
      TypeDump td = null!;
      Retry(() =>
      {
        try
        {
          od = _app.Communicator.DumpObject(remoteAddress, typeName, true, hashCode);

          // Check if we have a cached RemoteType with its SourceTypeDump.
          RemoteType rt = TypeResolver.Instance.ResolveRemoteType(_app, od.Type);
          if (rt != null)
          {
            td = rt.SourceTypeDump;
            return; // Use TypeDump from cached RemoteType
          }

          // Type not in cache - fetch full type info from Diver
          td = _app.Communicator.DumpType(od.Type);
        }
        catch (Exception e)
        {
          throw new InvalidOperationException(
            $"Could not dump remote object {typeName} at address {remoteAddress:X}.\n" +
            $"This is likely due to the object being invalid or not being a managed object.",
            e);
        }
      }, delay: 10, raise: true);

      // The requested heap address may differ from Diver's stable pin token.
      // Publish by the returned token so every local wrapper shares one ref.
      // A current Diver took one reference (the new ref's, or a surplus to
      // return); an older one may not have, and then nothing is returned.
      var receipt = od.ReferenceTaken ? PinReceipt.Surplus : PinReceipt.None;
      return PublishRemoteObjectRef(
        od.PinnedAddress,
        () => new RemoteObjectRef(od, td, _app.Communicator),
        ref receipt);
    }


    /// <summary>
    /// Creates a RemoteObject from field response data without calling /object.
    /// Uses the lightweight RemoteObjectRef constructor.
    /// Reuses existing RemoteObjectRef if one is already cached for this token.
    /// </summary>
    private RemoteObject GetRemoteObjectFromFieldUncached(
      ulong pinnedAddress,
      string typeName,
      ref PinReceipt receipt)
    {
      if (TryGetLiveRemoteObjectRef(pinnedAddress, out var existingRef))
      {
        try
        {
          var shared = new RemoteObject(existingRef, _app);
          SettleReceipt(pinnedAddress, ref receipt);
          return shared;
        }
        catch (ObjectDisposedException)
        {
          // Released meanwhile: publish below, adopting this receipt's reference.
        }
      }

      TypeDump td = null!;
      Retry(() =>
      {
        try
        {
          // Check if we have a cached RemoteType with its SourceTypeDump.
          RemoteType rt = TypeResolver.Instance.ResolveRemoteType(_app, typeName);
          if (rt != null)
          {
            td = rt.SourceTypeDump;
            return;
          }

          // Type not in cache - fetch type info from Diver
          td = _app.Communicator.DumpType(typeName);
        }
        catch (Exception e)
        {
          throw new InvalidOperationException(
            $"Could not resolve type {typeName} for object at address {pinnedAddress:X}.",
            e);
        }
      }, delay: 10, raise: true);

      return PublishRemoteObjectRef(
        pinnedAddress,
        () => new RemoteObjectRef(pinnedAddress, typeName, td, _app.Communicator),
        ref receipt);
    }


    public RemoteObject GetRemoteObject(
      ulong address,
      string typeName,
      int? hashcode = null)
    {
      // A cached holder takes no new Diver reference; a dump settles its own.
      if (TryAcquireCached(address, out var cached))
        return cached;
      return PublishToCache(address, GetRemoteObjectUncached(address, typeName, hashcode));
    }

    /// <summary>
    /// Gets a RemoteObject using the lightweight path (no /object call), for a
    /// token the Diver has just returned and so pinned once more for this
    /// client (invoke, field and item reads, object creation, batch items and
    /// callback arguments). The reference is taken over, or returned if this
    /// fails first.
    /// </summary>
    public RemoteObject GetRemoteObjectFromField(
      ulong pinnedAddress,
      string typeName) =>
      GetRemoteObjectFromField(pinnedAddress, typeName, ownsReference: true);

    /// <summary>
    /// As <see cref="GetRemoteObjectFromField(ulong, string)"/>; without
    /// <paramref name="ownsReference"/> (a callback argument a Diver pinned once
    /// for all recipients) nothing is returned: a new ref adopts that reference
    /// and an existing one leaves it.
    /// </summary>
    public RemoteObject GetRemoteObjectFromField(
      ulong pinnedAddress,
      string typeName,
      bool ownsReference)
    {
      var receipt = ownsReference ? PinReceipt.Surplus : PinReceipt.None;
      try
      {
        if (TryAcquireCached(pinnedAddress, out var cached))
        {
          SettleReceipt(pinnedAddress, ref receipt);
          return cached;
        }
        return PublishToCache(pinnedAddress,
          GetRemoteObjectFromFieldUncached(pinnedAddress, typeName, ref receipt));
      }
      finally
      {
        SettleReceipt(pinnedAddress, ref receipt); // only after a failure
      }
    }
  }

  private Process _procWithDiver;
  private readonly DomainDump _currentDomain;
  private readonly Dictionary<string, TypesDump> _remoteTypes = new();
  private readonly RemoteObjectsCollection _remoteObjects;

  public Process Process => _procWithDiver;
  public RemoteActivator Activator { get; private set; }
  public RemoteHarmony Harmony { get; private set; }

  private static DiverCommunicator _communicator;
  public DiverCommunicator Communicator => _communicator;
  public static bool IsReconnected = false;

  internal RemoteHandle(Process procWithDiver, DiverCommunicator communicator)
  {
    _procWithDiver = procWithDiver;
    _communicator = communicator;

    _currentDomain = communicator.DumpDomain();
    _remoteObjects = new RemoteObjectsCollection(this);
    Activator = new RemoteActivator(communicator, this);
    Harmony = new RemoteHarmony(this);
  }

  //
  // Init
  //

  /// <summary>
  /// Creates a new provider.
  /// </summary>
  /// <param name="target">Process to create the provider for</param>
  /// <returns>A provider for the given process</returns>
  public static RemoteHandle Connect(Process target)
  {
    return Connect(target, (ushort) target.Id);
  }

  public static RemoteHandle Connect(
    Process target,
    ushort diverPort,
    CancellationTokenSource? cts = null)
  {
    // Use discovery to check for existing diver
    string diverAddr = "127.0.0.1";
    switch (Bootstrapper.QueryStatus(target, diverAddr, diverPort))
    {
      case DiverState.NoDiver:
        // No diver, we need to inject one
        try
        {
          Bootstrapper.Inject(target, diverPort);
          break;
        }
        catch (Exception e)
        {
          throw new InvalidOperationException("Failed to inject diver.", e);
        }
      case DiverState.Alive:
        // Skip injection as diver assembly is already bootstrapped
        IsReconnected = true;
        break;
      case DiverState.Corpse:
        throw new InvalidOperationException("Diver could not finish bootstrapping.");
      case DiverState.HollowSnapshot:
        throw new InvalidOperationException("Target process is empty. Did you attach to the correct process?");
    }

    // Now register our program as a "client" of the diver
    DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort, cts);
    if (com.RegisterClient() == false)
    {
      string details = com.LastError is null ? "<no transport exception captured>" : com.LastError.ToString();
      throw new InvalidOperationException(
        $"Registering as a client in the Diver failed. PID={target.Id}, Port={diverPort}. Transport error: {details}");
    }

    return new RemoteHandle(target, com);
  }

  //
  // Remote Heap querying
  //

  public IEnumerable<CandidateType> QueryTypes(string typeFullName)
  {
    foreach (string assembly in _currentDomain.Modules)
    {
      if (!_remoteTypes.ContainsKey(assembly))
        continue;

      foreach (TypesDump.TypeIdentifiers type in _remoteTypes[assembly].Types)
      {
        if (type.TypeName == typeFullName)
          yield return new CandidateType(type.TypeName, assembly);
      }
    }
  }

  public IEnumerable<CandidateObject> QueryInstances(Type typeFilter, bool dumpHashcodes = true) =>
    QueryInstances(typeFilter.FullName, dumpHashcodes);

  /// <summary>
  /// Gets all object candidates for a specific filter
  /// </summary>
  /// <param name="typeFullNameFilter">Objects with Full Type Names of this EXACT string will be returned. You can use '*' as a "0 or more characters" wildcard</param>
  /// <param name="dumpHashcodes">Whether to also dump hashcodes of every matching object.
  /// This makes resolving the candidates later more reliable but for wide queries (e.g. "*") this might fail the entire search since it causes instabilities in the heap when examining it.
  /// </param>
  public IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter, bool dumpHashcodes = true)
  {
    return Communicator.DumpHeap(typeFullNameFilter, dumpHashcodes).Objects
      .Select(heapObj =>
        new CandidateObject(heapObj.Address, heapObj.Type, heapObj.HashCode));
  }

  //
  // Resolving Types
  //

  /// <summary>
  /// Gets a handle to a remote type (even ones from assemblies we aren't
  /// referencing/loading to the local process)
  /// </summary>
  /// <param name="typeFullName">Full name of the type to get. For example 'System.Xml.XmlDocument'</param>
  /// <param name="assembly">Optional short name of the assembly containing the type. For example 'System.Xml.ReaderWriter.dll'</param>
  /// <returns></returns>
  public Type GetRemoteType(string typeFullName, string assembly = null)
  {
    var resolver = TypeResolver.Instance;

    // When assembly is specified, we can try cache/local resolution first.
    // When assembly is null, we MUST dump from remote to get actual method info,
    // as local reference assemblies only have stub methods.
    if (assembly != null)
    {
      Type res = resolver.Resolve(this, assembly, typeFullName);
      if (res != null)
      {
        // Either found in cache or found locally.

        // If it's a local type we need to wrap it in a "fake" RemoteType (So
        // method invocations will actually happened in the remote app, for
        // example) (But not for primitives...)
        if (!(res is RemoteType) && !res.IsPrimitive)
        {
          res = new RemoteType(this, res);
          // TODO: Registering here in the cache is a hack but we couldn't
          // register within "TypeResolver.Resolve" because we don't have the
          // RemoteHandle to associate the fake remote type with.
          // Maybe this should move somewhere else...
          resolver.RegisterType(res);
        }

        return res;
      }
    }
    // Dump the remote type. This takes much more time (includes
    // dumping of dependent types) and should be avoided as much as possible.
    RemoteTypesFactory rtf = new RemoteTypesFactory(resolver, Communicator);
    var dumpedType = Communicator.DumpType(typeFullName, assembly);

    return rtf.Create(this, dumpedType);
  }

  /// <summary>
  /// Returns a handle to a remote type based on a given local type.
  /// </summary>
  public Type GetRemoteType(Type localType) =>
    GetRemoteType(localType.FullName, localType.Assembly.GetName().Name);
  public Type GetRemoteType(CandidateType candidate) =>
    GetRemoteType(candidate.TypeFullName, candidate.Assembly);
  internal Type GetRemoteType(TypeDump typeDump)
  {
    var resolver = TypeResolver.Instance;

    // Check cache for an already-complete RemoteType (built from TypeDump)
    Type cached = resolver.Resolve(this, typeDump.Assembly, typeDump.Type);
    if (cached is RemoteType rt && rt.SourceTypeDump != null)
      return rt;

    //
    // Use TypeDump directly via RemoteTypesFactory.
    //
    // Don't fall back to local reference assemblies which only contain public
    // stubs and would lose private member metadata from the Diver.
    //
    RemoteTypesFactory rtf = new RemoteTypesFactory(resolver, Communicator);
    return rtf.Create(this, typeDump);
  }

  public RemoteEnum GetRemoteEnum(string typeFullName, string assembly = null)
  {
    RemoteType remoteType = GetRemoteType(typeFullName, assembly) as RemoteType
      ?? throw new Exception("Failed to dump remote enum (and get a RemoteType object)");
    return new RemoteEnum(remoteType);
  }

  //
  // Getting Remote Objects
  //

  public RemoteObject GetRemoteObject(CandidateObject candidate) =>
    GetRemoteObject(candidate.Address, candidate.TypeFullName, candidate.HashCode);

  public RemoteObject GetRemoteObject(
    ulong remoteAddress,
    string typeName,
    int? hashCode = null)
  {
    return _remoteObjects.GetRemoteObject(remoteAddress, typeName, hashCode);
  }

  /// <summary>
  /// Gets a RemoteObject using the lightweight path (no /object call).
  /// The object is already pinned by the /get_field caller.
  /// </summary>
  public RemoteObject GetRemoteObjectFromField(
    ulong pinnedAddress,
    string typeName)
  {
    return _remoteObjects.GetRemoteObjectFromField(pinnedAddress, typeName);
  }

  /// <summary>
  /// Gets a RemoteObject for a callback argument; <paramref name="ownsReference"/>
  /// is whether the Diver pinned it for this client (see
  /// <see cref="Hooking.HookContext.OwnsParameters"/>).
  /// </summary>
  internal RemoteObject GetRemoteObjectFromField(
    ulong pinnedAddress,
    string typeName,
    bool ownsReference)
  {
    return _remoteObjects.GetRemoteObjectFromField(pinnedAddress, typeName, ownsReference);
  }

  public RemoteObjectCacheDiagnostics GetRemoteObjectCacheDiagnostics() =>
    _remoteObjects.GetDiagnostics();

  //
  // IDisposable
  //
  public void Dispose()
  {
    _remoteObjects.Clear();
    _communicator?.Disconnect();
    _communicator = null;
    _procWithDiver = null;

    // Clear global type cache
    TypeResolver.Instance.ClearCache();
    _remoteTypes.Clear();
  }
}
