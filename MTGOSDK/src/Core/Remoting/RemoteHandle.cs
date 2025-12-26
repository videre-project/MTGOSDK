/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Structs;
using MTGOSDK.Core.Remoting.Types;
using MTGOSDK.Resources;


namespace MTGOSDK.Core.Remoting;

public class RemoteHandle : DLRWrapper, IDisposable
{
  internal class RemoteObjectsCollection
  {
    // Lock-free cache using ConcurrentDictionary for better concurrent performance
    private readonly ConcurrentDictionary<ulong, Lazy<WeakReference<RemoteObject>>> _pinnedAddressesToRemoteObjects;
    private readonly RemoteHandle _app;

    public RemoteObjectsCollection(RemoteHandle app)
    {
      _app = app;
      _pinnedAddressesToRemoteObjects = new ConcurrentDictionary<ulong, Lazy<WeakReference<RemoteObject>>>();
    }

    private RemoteObject GetRemoteObjectUncached(
      ulong remoteAddress,
      string typeName,
      int? hashCode = null)
    {
      ObjectDump od = null!;
      TypeDump td = null!;
      Retry(() =>
      {
        try
        {
          od = _app.Communicator.DumpObject(remoteAddress, typeName, true, hashCode);

          // Check if we have a cached RemoteType with its SourceTypeDump.
          Type cachedType = TypeResolver.Instance.Resolve(null, od.Type);
          if (cachedType is RemoteType rt && rt.SourceTypeDump != null)
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

      var objRef = new RemoteObjectRef(od, td, _app.Communicator);
      var remoteObject = new RemoteObject(objRef, _app);

      return remoteObject;
    }

    public RemoteObject GetRemoteObject(
      ulong address,
      string typeName,
      int? hashcode = null)
    {
      const int maxRetries = 5;

      for (int attempt = 0; attempt < maxRetries; attempt++)
      {
        // Fast path: try to get existing valid reference
        if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out var existingLazy))
        {
          var weakRef = existingLazy.Value;
          if (weakRef.TryGetTarget(out var ro) && ro.IsValid)
          {
            ro.AddReference();
            if (ro.IsValid) return ro;
            ro.ReleaseReference();
          }
          // Stale entry - remove it and retry
          _pinnedAddressesToRemoteObjects.TryRemove(address, out _);
          continue;
        }

        // Slow path: create new object and try to add it
        var newRo = GetRemoteObjectUncached(address, typeName, hashcode);
        var newLazy = new Lazy<WeakReference<RemoteObject>>(
          () => new WeakReference<RemoteObject>(newRo),
          LazyThreadSafetyMode.ExecutionAndPublication);

        // Try to add our new object
        if (_pinnedAddressesToRemoteObjects.TryAdd(address, newLazy))
        {
          // We won - return our object
          return newRo;
        }

        // Lost race - suppress unpin on discarded object
        newRo.SuppressUnpin();
      }

      // Exhausted retries - create object directly without caching
      return GetRemoteObjectUncached(address, typeName, hashcode);
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
        catch (IOException e)
        {
          throw new Exception("Failed to inject diver.", e);
        }
      case DiverState.Alive:
        // Skip injection as diver assembly is already bootstrapped
        IsReconnected = true;
        break;
      case DiverState.Corpse:
        throw new Exception("Diver could not finish bootstrapping.");
      case DiverState.HollowSnapshot:
        throw new Exception("Target process is empty. Did you attach to the correct process?");
    }

    // Now register our program as a "client" of the diver
    DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort, cts);
    if (com.RegisterClient() == false)
      throw new Exception("Registering as a client in the Diver failed.");

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
      Type res = resolver.Resolve(assembly, typeFullName);
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
    else
    {
      // Check cache first for already-dumped remote types, but skip local resolution
      Type cached = resolver.Resolve(null, typeFullName);
      if (cached is RemoteType)
      {
        return cached;
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
  internal Type GetRemoteType(TypeDump typeDump) =>
    GetRemoteType(typeDump.Type, typeDump.Assembly);

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

  //
  // IDisposable
  //
  public void Dispose()
  {
    _communicator?.Disconnect();
    _communicator = null;
    _procWithDiver = null;

    // Clear global type cache
    TypeResolver.Instance.ClearCache();
    _remoteTypes.Clear();
  }
}
