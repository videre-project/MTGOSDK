using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Utils;

using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;


namespace RemoteNET
{
  public class RemoteApp : IDisposable
  {
    internal class RemoteObjectsCollection
    {
      // The WeakReferences are to RemoteObject
      private readonly Dictionary<ulong, WeakReference<RemoteObject>> _pinnedAddressesToRemoteObjects;
      private readonly object _lock = new object();

      private readonly RemoteApp _app;

      public RemoteObjectsCollection(RemoteApp app)
      {
        _app = app;
        _pinnedAddressesToRemoteObjects = new Dictionary<ulong, WeakReference<RemoteObject>>();
      }

      private RemoteObject GetRemoteObjectUncached(
        ulong remoteAddress,
        string typeName,
        int? hashCode = null)
      {
        ObjectDump od;
        TypeDump td;
        try
        {
          od = _app._communicator.DumpObject(remoteAddress, typeName, true, hashCode);
          td = _app._communicator.DumpType(od.Type);
        }
        catch (Exception e)
        {
          throw new Exception("Could not dump remote object/type.", e);
        }

        var remoteObject = new RemoteObject(
            new RemoteObjectRef(od, td, _app._communicator), _app);

        return remoteObject;
      }

      public RemoteObject GetRemoteObject(
        ulong address,
        string typeName,
        int? hashcode = null)
      {
        RemoteObject ro;
        WeakReference<RemoteObject> weakRef;
        // Easiert way - Non-collected and previouslt obtained object ("Cached")
        if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out weakRef) &&
          weakRef.TryGetTarget(out ro))
        {
          // Not GC'd!
          return ro;
        }

        // Harder case - At time of checking, item wasn't cached.
        // We need exclusive access to the cahce now to make sure we are the only one adding it.
        lock (_lock)
        {
          // Last chance - when we waited on the lock some other thread might've added it to the cache.
          if (_pinnedAddressesToRemoteObjects.TryGetValue(address, out weakRef))
          {
            bool gotTarget = weakRef.TryGetTarget(out ro);
            if (gotTarget)
            {
              // Not GC'd!
              return ro;
            }
            else
            {
              // Object was GC'd...
              _pinnedAddressesToRemoteObjects.Remove(address);
              // Now let's make sure the GC'd object finalizer was also called (otherwise some "object moved" errors might happen).
              GC.WaitForPendingFinalizers();
              // Now we need to-read the remote object since stuff might have moved
            }
          }

          // Get remote
          ro = GetRemoteObjectUncached(address, typeName, hashcode);
          // Add to cache
          weakRef = new WeakReference<RemoteObject>(ro);
          _pinnedAddressesToRemoteObjects[ro.RemoteToken] = weakRef;
        }

        return ro;
      }
    }

    private Process _procWithDiver;
    private DiverCommunicator _communicator;
    private DomainsDump _domains;
    private readonly RemoteObjectsCollection _remoteObjects;

    public Process Process => _procWithDiver;
    public RemoteActivator Activator { get; private set; }
    public RemoteHarmony Harmony { get; private set; }

    public DiverCommunicator Communicator => _communicator;

    private RemoteApp(Process procWithDiver, DiverCommunicator communicator)
    {
      _procWithDiver = procWithDiver;
      _communicator = communicator;
      Activator = new RemoteActivator(communicator, this);
      Harmony = new RemoteHarmony(this);
      _remoteObjects = new RemoteObjectsCollection(this);
    }

    //
    // Init
    //

    /// <summary>
    /// Creates a new provider.
    /// </summary>
    /// <param name="target">Process to create the provider for</param>
    /// <returns>A provider for the given process</returns>
    public static RemoteApp Connect(Process target)
    {
      return Connect(target, (ushort)target.Id);
    }

    public static RemoteApp Connect(Process target, ushort diverPort)
    {
      // Use discovery to check for existing diver
      string diverAddr = "127.0.0.1";
      switch(DiverDiscovery.QueryStatus(target, diverAddr, diverPort))
      {
        case DiverState.NoDiver:
          // No diver, we need to inject one
          Bootstrapper.Inject(target, diverPort);
          break;
        case DiverState.Corpse:
          // Diver is dead but we can't inject a new one
          throw new Exception("Diver is not responding to HTTP requests.");
      }

      // Now register our program as a "client" of the diver
      DiverCommunicator com = new DiverCommunicator(diverAddr, diverPort);
      if (com.RegisterClient() == false)
        throw new Exception("Registering as a client in the Diver failed.");

      return new RemoteApp(target, com);
    }

    //
    // Remote Heap querying
    //

    public IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter)
    {
      Predicate<string> matchesFilter = Filter.CreatePredicate(typeFullNameFilter);

      _domains ??= _communicator.DumpDomains();
      foreach (DomainsDump.AvailableDomain domain in _domains.AvailableDomains)
      {
        foreach (string assembly in domain.AvailableModules)
        {
          List<TypesDump.TypeIdentifiers> typeIdentifiers;
          try
          {
            typeIdentifiers = _communicator.DumpTypes(assembly).Types;
          }
          catch
          {
            // TODO:
            Debug.WriteLine($"[{nameof(RemoteApp)}][{nameof(QueryTypes)}] Exception thrown when Dumping/Iterating assembly: {assembly}");
            continue;
          }
          foreach (TypesDump.TypeIdentifiers type in typeIdentifiers)
          {
            // TODO: Filtering should probably be done in the Diver's side
            if (matchesFilter(type.TypeName))
              yield return new CandidateType(type.TypeName, assembly);
          }

        }
      }

    }

    public IEnumerable<CandidateObject> QueryInstances(Type typeFilter, bool dumpHashcodes = true) => QueryInstances(typeFilter.FullName, dumpHashcodes);

    /// <summary>
    /// Gets all object candidates for a specific filter
    /// </summary>
    /// <param name="typeFullNameFilter">Objects with Full Type Names of this EXACT string will be returned. You can use '*' as a "0 or more characters" wildcard</param>
    /// <param name="dumpHashcodes">Whether to also dump hashcodes of every matching object.
    /// This makes resolving the candidates later more reliable but for wide queries (e.g. "*") this might fail the entire search since it causes instabilities in the heap when examining it.
    /// </param>
    public IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter, bool dumpHashcodes = true)
    {
      return _communicator.DumpHeap(typeFullNameFilter, dumpHashcodes).Objects
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
      // Easy case: Trying to resolve from cache or from local assemblies
      var resolver = TypesResolver.Instance;
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
          // register within "TypesResolver.Resolve" because we don't have the
          // RemoteApp to associate the fake remote type with.
          // Maybe this should move somewhere else...
          resolver.RegisterType(res);
        }
        return res;
      }

      // Harder case: Dump the remote type. This takes much more time (includes
      // dumping of dependent types) and should be avoided as much as possible.
      RemoteTypesFactory rtf = new RemoteTypesFactory(resolver, _communicator,
          avoidGenericsRecursion: true);
      var dumpedType = _communicator.DumpType(typeFullName, assembly);
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

    /// <summary>
    /// Loads an assembly into the remote process
    /// </summary>
    public bool LoadAssembly(Assembly assembly) => LoadAssembly(assembly.Location);

    /// <summary>
    /// Loads an assembly into the remote process
    /// </summary>
    public bool LoadAssembly(string path)
    {
      bool res = _communicator.InjectDll(path);
      if (res)
      {
        // Resetting the cached domains because otherwise we won't see our newly
        // injected module
        _domains = null;
      }
      return res;
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

    //
    // IDisposable
    //
    public void Dispose()
    {
      Communicator?.KillDiver();
      _communicator = null;
      _procWithDiver = null;
    }
  }
}
