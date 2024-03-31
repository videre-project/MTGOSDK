/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;

using Exception = System.Exception;

using MTGOSDK.Win32.API;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Extensions;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;
using MTGOSDK.Core.Remoting.Interop.Utils;

using ScubaDiver.Utils;


namespace ScubaDiver;

public class Diver : IDisposable
{
  // Runtime analysis and exploration fields
  private readonly object _clrMdLock = new();
  private DataTarget _dt = null;
  private ClrRuntime _runtime = null;
  // Address to Object converter
  private readonly Converter<object> _converter = new();

  // Clients Tracking
  public object _registeredPidsLock = new();
  public List<int> _registeredPids = new();

  // HTTP Responses fields
  private readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

  // Callbacks Endpoint of the Controller process
  private bool _monitorEndpoints = true;
  private int _nextAvailableCallbackToken;
  private readonly UnifiedAppDomain _unifiedAppDomain;
  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;

  // Object freezing (pinning)
  private readonly FrozenObjectsCollection _freezer = new();

  private readonly ManualResetEvent _stayAlive = new(true);

  public Diver()
  {
    _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
    {
      // Diver maintenance
      {"/ping", MakePingResponse},
      {"/die", MakeDieResponse},
      {"/register_client", MakeRegisterClientResponse},
      {"/unregister_client", MakeUnregisterClientResponse},
      // Dumping
      {"/domains", MakeDomainsResponse},
      {"/heap", MakeHeapResponse},
      {"/types", MakeTypesResponse},
      {"/type", MakeTypeResponse},
      // Remote Object API
      {"/object", MakeObjectResponse},
      {"/create_object", MakeCreateObjectResponse},
      {"/invoke", MakeInvokeResponse},
      {"/get_field", MakeGetFieldResponse},
      {"/set_field", MakeSetFieldResponse},
      {"/unpin", MakeUnpinResponse},
      {"/event_subscribe", MakeEventSubscribeResponse},
      {"/event_unsubscribe", MakeEventUnsubscribeResponse},
      {"/get_item", MakeArrayItemResponse},
    };
    _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
    _unifiedAppDomain = new UnifiedAppDomain(this);
  }

  #region Bootstrapper Cleanup

  private static bool UnloadBootstrapper()
  {
    foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
    {
      if (new string[] {
          "Bootstrapper.dll",
          "Bootstrapper_x64.dll"
        }.Any(s => module.ModuleName == s))
      {
        return Kernel32.FreeLibrary(module.BaseAddress);
      }
    }
    return false;
  }

  #endregion

  public void Start(ushort listenPort)
  {
    // Start session
    RefreshRuntime();
    HttpListener listener = new();
    string listeningUrl = $"http://127.0.0.1:{listenPort}/";
    listener.Prefixes.Add(listeningUrl);
    // Set timeout
    var manager = listener.TimeoutManager;
    manager.IdleConnection = TimeSpan.FromSeconds(5);
    listener.Start();
    Logger.Debug($"[Diver] Listening on {listeningUrl}...");

    // Unload the native bootstrapper DLL to free up the file handle.
    bool hr = UnloadBootstrapper();
    if (hr != true)
    {
      Logger.Debug("[EntryPoint] Failed to unload Bootstrapper.");
    }

    Task endpointsMonitor = Task.Run(CallbacksEndpointsMonitor);
    Dispatcher(listener);
    Logger.Debug("[Diver] Stopping Callback Endpoints Monitor");
    _monitorEndpoints = false;
    try { endpointsMonitor.Wait(); } catch { }

    Logger.Debug("[Diver] Closing listener");
    listener.Stop();
    listener.Close();
    Logger.Debug("[Diver] Closing ClrMD runtime and snapshot");
    DisposeRuntime();

    Logger.Debug("[Diver] Unpinning objects");
    _freezer.UnpinAll();
    Logger.Debug("[Diver] Unpinning finished");

    Logger.Debug("[Diver] Dispatcher returned, Start is complete.");
  }

  private void CallbacksEndpointsMonitor()
  {
    while (_monitorEndpoints)
    {
      Thread.Sleep(TimeSpan.FromSeconds(1));
      IPEndPoint endpoint;
      foreach (var registeredEventHandlerInfo in _remoteEventHandler)
      {
        endpoint = registeredEventHandlerInfo.Value.Endpoint;
        ReverseCommunicator reverseCommunicator = new(endpoint);
        Logger.Debug($"[Diver] Checking if callback client at {endpoint} is alive. Token = {registeredEventHandlerInfo.Key}. Type = Event");
        bool alive = reverseCommunicator.CheckIfAlive();
        Logger.Debug($"[Diver] Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) is alive = {alive}");
        if (!alive)
        {
          Logger.Debug(
            $"[Diver] Dead Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) DROPPED!");
          _remoteEventHandler.TryRemove(registeredEventHandlerInfo.Key, out _);
        }
      }
    }
  }

  #region Helpers

  [DllImport("kernel32.dll")]
  private static extern int PssFreeSnapshot(
    IntPtr ProcessHandle,
    IntPtr SnapshotHandle
  );

  /// <summary>
  /// Disposes of the DataTarget instance without calling any Trace methods.
  /// </summary>
  private void DisposeDataTarget()
  {
    lock (_clrMdLock)
    {
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
          var id = int.Parse(dr.DisplayName.Substring("pid:".Length),
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
              if (PssFreeSnapshot(proc.Handle, _snapshotHandle) != 0)
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

  private void DisposeCLRRuntime()
  {
    lock (_clrMdLock)
    {
      _runtime?.Dispose();
      _runtime = null;
    }
  }

  private void DisposeRuntime()
  {
    DisposeDataTarget();
    DisposeCLRRuntime();
  }

  private void RefreshRuntime()
  {
    // Refresh the runtime and update the last refresh time
    DisposeRuntime();
    lock (_clrMdLock)
    {
      // This works like 'fork()': it creates a secondary process which is a
      // copy of the current one, but without any running threads.
      // Then our process attaches to the other one and reads its memory.
      //
      // NOTE: This subprocess inherits handles to DLLs in the current process
      // so it might "lock" both the Bootstrapper and ScubaDiver dlls.
      _dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
      _runtime = _dt.ClrVersions.Single().CreateRuntime();
    }
  }

  private object ParseParameterObject(ObjectOrRemoteAddress param)
  {
    switch (param)
    {
      case { IsNull: true }:
        return null;
      case { IsType: true }:
        return _unifiedAppDomain.ResolveType(param.Type, param.Assembly);
      case { IsRemoteAddress: false }:
        return PrimitivesEncoder.Decode(param.EncodedObject, param.Type);
      case { IsRemoteAddress: true }:
        if (_freezer.TryGetPinnedObject(param.RemoteAddress, out object pinnedObj))
        {
          return pinnedObj;
        }
        break;
    }

    Debugger.Launch();
    throw new NotImplementedException(
      $"Don't know how to parse this parameter into an object of type `{param.Type}`");
  }

  public string QuickError(string error, string stackTrace = null)
  {
    if (stackTrace == null)
    {
      stackTrace = (new StackTrace(true)).ToString();
    }
    DiverError errResults = new(error, stackTrace);
    return JsonConvert.SerializeObject(errResults);
  }

  #endregion

  #region HTTP Dispatching
  private void HandleDispatchedRequest(HttpListenerContext requestContext)
  {
    HttpListenerRequest request = requestContext.Request;

    var response = requestContext.Response;
    string body;
    if (_responseBodyCreators.TryGetValue(request.Url.AbsolutePath, out var respBodyGenerator))
    {
      try
      {
        body = respBodyGenerator(request);
      }
      catch (Exception ex)
      {
        body = QuickError(ex.Message, ex.StackTrace);
      }
    }
    else
    {
      body = QuickError("Unknown Command");
    }

    byte[] buffer = Encoding.UTF8.GetBytes(body);
    // Get a response stream and write the response to it.
    response.ContentLength64 = buffer.Length;
    response.ContentType = "application/json";
    Stream output = response.OutputStream;
      output.Write(buffer, 0, buffer.Length);
    // You must close the output stream.
    output.Close();
  }

  private void Dispatcher(HttpListener listener)
  {
    // Using a timeout we can make sure not to block if the
    // 'stayAlive' state changes to "reset" (which means we should die)
    while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
    {
      void ListenerCallback(IAsyncResult result)
      {
        HttpListener listener = (HttpListener)result.AsyncState;
        HttpListenerContext context;
        try
        {
          context = listener.EndGetContext(result);
        }
        catch (ObjectDisposedException)
        {
          Logger.Debug("[Diver][ListenerCallback] Listener was disposed. Exiting.");
          return;
        }
        catch (HttpListenerException e)
        {
          if (e.Message.StartsWith("The I/O operation has been aborted"))
          {
            Logger.Debug($"[Diver][ListenerCallback] Listener was aborted. Exiting.");
            return;
          }
          throw;
        }

        try
        {
          HandleDispatchedRequest(context);
        }
        catch (Exception e)
        {
          Logger.Debug("[Diver] Task faulted! Exception:");
          Logger.Debug(e.ToString());
        }
      }
      IAsyncResult asyncOperation = listener.BeginGetContext(ListenerCallback, listener);

      while (true)
      {
        if (asyncOperation.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
        {
          // Async operation started! We can mov on to next request
          break;
        }
        else
        {
          // Async event still awaiting new HTTP requests... It's a good time to check
          // if we were signaled to die
          if (!_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
          {
            // Time to die.
            // Leaving the inner loop will get us to the outter loop where _stayAlive is checked (again)
            // and then it that loop will stop as well.
            break;
          }
          else
          {
            // No singal of die command. We can continue waiting
            continue;
          }
        }
      }
    }

    Logger.Debug("[Diver] HTTP Loop ended. Cleaning up");

    Logger.Debug("[Diver] Removing all event subscriptions");
    foreach (RegisteredEventHandlerInfo rehi in _remoteEventHandler.Values)
    {
      rehi.EventInfo.RemoveEventHandler(rehi.Target, rehi.RegisteredProxy);
    }
    _remoteEventHandler.Clear();
    Logger.Debug("[Diver] Removed all event subscriptions");
  }
  #endregion

  #region Object Pinning
  public (object instance, ulong pinnedAddress) GetObject(
    ulong objAddr,
    bool pinningRequested,
    string typeName,
    int? hashcode = null)
  {
    bool hashCodeFallback = hashcode.HasValue;

    // Check if we have this objects in our pinned pool
    if (_freezer.TryGetPinnedObject(objAddr, out object pinnedObj))
    {
      // Found pinned object!
      return (pinnedObj, objAddr);
    }

    //
    // The object is not pinned, so falling back to the last dumped runtime can
    // help ensure we can find the object by it's type information if it moves.
    //
    // For now, this only checks that the object is still in place.
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
      instance = _converter.ConvertFromIntPtr(finalObjAddress, methodTable);
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
      throw new Exception("A GC occurened between checking the CLR MD (twice) and the object retrieval." +
                "A different object was retrieved and its type is not the one we expected." +
                $"Expected Type: {typeName}, Actual Type: {finalTypeName}");


    // Pin the result object if requested
    ulong pinnedAddress = 0;
    if (pinningRequested)
    {
      pinnedAddress = _freezer.Pin(instance);
    }
    return (instance, pinnedAddress);
  }
  #endregion

  public (bool anyErrors, List<HeapDump.HeapObject> objects) GetHeapObjects(
    Predicate<string> filter,
    bool dumpHashcodes)
  {
    List<HeapDump.HeapObject> objects = new();
    bool anyErrors = false;
    // Trying several times to dump all candidates
    for (int i = 0; i < 10; i++)
    {
      Logger.Debug($"Trying to dump heap objects. Try #{i + 1}");
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
                instance = _converter.ConvertFromIntPtr(clrObj.Address, mt);
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
      Logger.Debug($"Failt to dump heap objects. Aborting.");
      objects.Clear();
    }
    return (anyErrors, objects);
  }

  #region Ping Handler

  private string MakePingResponse(HttpListenerRequest arg)
  {
    return "{\"status\":\"pong\"}";
  }

  #endregion

  #region Client Registration Handlers
  private string MakeRegisterClientResponse(HttpListenerRequest arg)
  {
    string pidString = arg.QueryString.Get("process_id");
    if (pidString == null || !int.TryParse(pidString, out int pid))
    {
      return QuickError("Missing parameter 'process_id'");
    }
    lock (_registeredPidsLock)
    {
      _registeredPids.Add(pid);
    }
    Logger.Debug("[Diver] New client registered. ID = " + pid);
    return "{\"status\":\"OK'\"}";
  }
  private string MakeUnregisterClientResponse(HttpListenerRequest arg)
  {
    string pidString = arg.QueryString.Get("process_id");
    if (pidString == null || !int.TryParse(pidString, out int pid))
    {
      return QuickError("Missing parameter 'process_id'");
    }
    bool removed;
    int remaining;
    lock (_registeredPidsLock)
    {
      removed = _registeredPids.Remove(pid);
      remaining = _registeredPids.Count;
    }
    Logger.Debug("[Diver] Client unregistered. ID = " + pid);

    UnregisterClientResponse ucResponse = new()
    {
      WasRemoved = removed,
      OtherClientsAmount = remaining
    };

    return JsonConvert.SerializeObject(ucResponse);
  }

  #endregion

  private string MakeDomainsResponse(HttpListenerRequest req)
  {
    List<DomainsDump.AvailableDomain> available = new();
    lock (_clrMdLock)
    {
      foreach (ClrAppDomain clrAppDomain in _runtime.AppDomains)
      {
        var modules = clrAppDomain.Modules
          .Select(m => Path.GetFileNameWithoutExtension(m.Name))
          .Where(m => !string.IsNullOrWhiteSpace(m))
          .ToList();
        var dom = new DomainsDump.AvailableDomain()
        {
          Name = clrAppDomain.Name,
          AvailableModules = modules
        };
        available.Add(dom);
      }
    }

    DomainsDump dd = new()
    {
      Current = AppDomain.CurrentDomain.FriendlyName,
      AvailableDomains = available
    };

    return JsonConvert.SerializeObject(dd);
  }

  private string MakeTypesResponse(HttpListenerRequest req)
  {
    string assembly = req.QueryString.Get("assembly");
    Assembly matchingAssembly = _unifiedAppDomain.GetAssembly(assembly);
    if (matchingAssembly == null)
      return QuickError($"No assemblies found matching the query '{assembly}'");

    List<TypesDump.TypeIdentifiers> types = new List<TypesDump.TypeIdentifiers>();
    foreach (Type type in matchingAssembly.GetTypes())
    {
      types.Add(new TypesDump.TypeIdentifiers() { TypeName = type.FullName });
    }

    TypesDump dump = new() { AssemblyName = assembly, Types = types };
    return JsonConvert.SerializeObject(dump);
  }

  public string MakeTypeResponse(TypeDumpRequest dumpRequest)
  {
    string type = dumpRequest.TypeFullName;
    if (string.IsNullOrEmpty(type))
    {
      return QuickError("Missing parameter 'TypeFullName'");
    }

    string assembly = dumpRequest.Assembly;
    //Logger.Debug($"[Diver] Trying to dump Type: {type}");
    if (assembly != null)
    {
      //Logger.Debug($"[Diver] Trying to dump Type: {type}, WITH Assembly: {assembly}");
    }
    Type resolvedType = null;
    lock (_clrMdLock)
    {
      resolvedType = _unifiedAppDomain.ResolveType(type, assembly);
    }

    //
    // Defining a sub-function that parses a type and it's parents recursively
    //
    static TypeDump ParseType(Type typeObj)
    {
      if (typeObj == null) return null;

      var ctors = typeObj.GetConstructors((BindingFlags)0xffff).Select(ci => new TypeDump.TypeMethod(ci))
        .ToList();
      var methods = typeObj.GetRuntimeMethods().Select(mi => new TypeDump.TypeMethod(mi))
        .ToList();
      var fields = typeObj.GetRuntimeFields().Select(fi => new TypeDump.TypeField(fi))
        .ToList();
      var events = typeObj.GetRuntimeEvents().Select(ei => new TypeDump.TypeEvent(ei))
        .ToList();
      var props = typeObj.GetRuntimeProperties().Select(pi => new TypeDump.TypeProperty(pi))
        .ToList();

      TypeDump td = new()
      {
        Type = typeObj.FullName,
        Assembly = typeObj.Assembly.GetName().Name,
        Methods = methods,
        Constructors = ctors,
        Fields = fields,
        Events = events,
        Properties = props,
        IsArray = typeObj.IsArray,
      };
      if (typeObj.BaseType != null)
      {
        // Has parent. Add its identifier
        td.ParentFullTypeName = typeObj.BaseType.FullName;
        td.ParentAssembly = typeObj.BaseType.Assembly.GetName().Name;
      }

      return td;
    }

    if (resolvedType != null)
    {
      TypeDump recusiveTypeDump = ParseType(resolvedType);
      return JsonConvert.SerializeObject(recusiveTypeDump);
    }

    return QuickError("Failed to find type in searched assemblies");
  }

  private string MakeTypeResponse(HttpListenerRequest req)
  {
    string body = null;
    using (StreamReader sr = new(req.InputStream))
    {
      body = sr.ReadToEnd();
    }
    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    TextReader textReader = new StringReader(body);
    var request = JsonConvert.DeserializeObject<TypeDumpRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    return MakeTypeResponse(request);
  }

  private string MakeHeapResponse(HttpListenerRequest arg)
  {
    string filter = arg.QueryString.Get("type_filter");
    string dumpHashcodesStr = arg.QueryString.Get("dump_hashcodes");
    bool dumpHashcodes = dumpHashcodesStr?.ToLower() == "true";

               // Default filter - no filter. Just return everything.
    Predicate<string> matchesFilter = Filter.CreatePredicate(filter);

    (bool anyErrors, List<HeapDump.HeapObject> objects) = GetHeapObjects(matchesFilter, dumpHashcodes);
    if (anyErrors)
    {
      return "{\"error\":\"All dumping trials failed because at least 1 " +
           "object moved between the snapshot and the heap enumeration\"}";
    }

    HeapDump hd = new() { Objects = objects };

    var resJson = JsonConvert.SerializeObject(hd);
    return resJson;
  }

  #region Events Handlers

  private string MakeEventUnsubscribeResponse(HttpListenerRequest arg)
  {
    string tokenStr = arg.QueryString.Get("token");
    if (tokenStr == null || !int.TryParse(tokenStr, out int token))
    {
      return QuickError("Missing parameter 'address'");
    }
    Logger.Debug($"[Diver][MakeEventUnsubscribeResponse] Called! Token: {token}");

    if (_remoteEventHandler.TryRemove(token, out RegisteredEventHandlerInfo eventInfo))
    {
      eventInfo.EventInfo.RemoveEventHandler(eventInfo.Target, eventInfo.RegisteredProxy);
      return "{\"status\":\"OK\"}";
    }
    return QuickError("Unknown token for event callback subscription");
  }

  public class EventWrapper<T> where T : EventArgs
  {
    private readonly EventHandler m_HandlerToCall;

    public EventWrapper(EventHandler handler_to_call)
    {
      m_HandlerToCall = handler_to_call;
    }

    public void Handle(object sender, T args)
    {
      m_HandlerToCall.Invoke(sender, args);
    }
  }

  private string MakeEventSubscribeResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    string ipAddrStr = arg.QueryString.Get("ip");
    string portStr = arg.QueryString.Get("port");
    if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
    {
      return QuickError("Missing parameter 'address' (object address)");
    }
    if (!(IPAddress.TryParse(ipAddrStr, out IPAddress ipAddress) && int.TryParse(portStr, out int port)))
    {
      return QuickError("Failed to parse either IP Address ('ip' param) or port ('port' param)");
    }
    IPEndPoint endpoint = new IPEndPoint(ipAddress, port);
    Logger.Debug($"[Diver][Debug](RegisterEventHandler) objAddrStr={objAddr:X16}");

    // Check if we have this objects in our pinned pool
    if (!_freezer.TryGetPinnedObject(objAddr, out object target))
    {
      // Object not pinned, try get it the hard way
      return QuickError("Object at given address wasn't pinned (context: RegisterEventHandler)");
    }

    Type resolvedType = target.GetType();

    string eventName = arg.QueryString.Get("event");
    if (eventName == null)
    {
      return QuickError("Missing parameter 'event'");
    }
    // TODO: Does this need to be done recursivly?
    EventInfo eventObj = resolvedType.GetEvent(eventName);
    if (eventObj == null)
    {
      return QuickError("Failed to find event in type");
    }

    // Let's make sure the event's delegate type has 2 args - (object, EventArgs or subclass)
    Type eventDelegateType = eventObj.EventHandlerType;
    MethodInfo invokeInfo = eventDelegateType.GetMethod("Invoke");
    ParameterInfo[] paramInfos = invokeInfo.GetParameters();
    if (paramInfos.Length != 2)
    {
      return QuickError("Currently only events with 2 parameters (object & EventArgs) can be subscribed to.");
    }

    int token = AssignCallbackToken();
    EventHandler eventHandler = (obj, args) => InvokeControllerCallback(endpoint, token, "UNUSED", new object[2] { obj, args });
    try
    {
      Type eventArgsType = paramInfos[1].ParameterType;
      var wrapperType = typeof(EventWrapper<>).MakeGenericType(eventArgsType);
      var wrapperInstance = Activator.CreateInstance(wrapperType, eventHandler);
      Delegate my_delegate = Delegate.CreateDelegate(eventDelegateType, wrapperInstance, "Handle");

      Logger.Debug($"[Diver] Adding event handler to event {eventName}...");
      eventObj.AddEventHandler(target, my_delegate);
      Logger.Debug($"[Diver] Added event handler to event {eventName}!");

      // Save all the registeration info so it can be removed later upon request
      _remoteEventHandler[token] = new RegisteredEventHandlerInfo()
      {
        EventInfo = eventObj,
        Target = target,
        RegisteredProxy = my_delegate,
        Endpoint = endpoint
      };
    }
    catch (Exception ex)
    {
      return QuickError($"Failed insert the event handler: {ex.ToString()}");
    }

    EventRegistrationResults erResults = new() { Token = token };
    return JsonConvert.SerializeObject(erResults);
  }

  public int AssignCallbackToken() =>
    Interlocked.Increment(ref _nextAvailableCallbackToken);

  public ObjectOrRemoteAddress InvokeControllerCallback(
    IPEndPoint callbacksEndpoint,
    int token,
    string stackTrace,
    params object[] parameters)
  {
    ReverseCommunicator reverseCommunicator = new(callbacksEndpoint);

    // Check if the client connection is still alive
    bool alive = reverseCommunicator.CheckIfAlive();
    if (!alive)
    {
      _remoteEventHandler.TryRemove(token, out _);
      return null;
    }

    var remoteParams = new ObjectOrRemoteAddress[parameters.Length];
    for (int i = 0; i < parameters.Length; i++)
    {
      object parameter = parameters[i];
      if (parameter == null)
      {
        remoteParams[i] = ObjectOrRemoteAddress.Null;
      }
      else if (parameter.GetType().IsPrimitiveEtc())
      {
        remoteParams[i] = ObjectOrRemoteAddress.FromObj(parameter);
      }
      else // Not primitive
      {
        // Check fi the object was pinned
        if (!_freezer.TryGetPinningAddress(parameter, out ulong addr))
        {
          // Pin and mark for unpinning later
          addr = _freezer.Pin(parameter);
        }

        remoteParams[i] = ObjectOrRemoteAddress.FromToken(addr, parameter.GetType().FullName);
      }
    }

    // Call callback at controller
    try
    {
      InvocationResults callbackResults = reverseCommunicator.InvokeCallback(
        token,
        stackTrace,
        remoteParams
      );

      return callbackResults.ReturnedObjectOrAddress;
    }
    catch (NullReferenceException) { }

    return null;
  }

  #endregion
  private string MakeObjectResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    string typeName = arg.QueryString.Get("type_name");
    bool pinningRequested = arg.QueryString.Get("pinRequest").ToUpper() == "TRUE";
    bool hashCodeFallback = arg.QueryString.Get("hashcode_fallback").ToUpper() == "TRUE";
    string hashCodeStr = arg.QueryString.Get("hashcode");
    int userHashcode = 0;
    if (objAddrStr == null)
    {
      return QuickError("Missing parameter 'address'");
    }
    if (!ulong.TryParse(objAddrStr, out var objAddr))
    {
      return QuickError("Parameter 'address' could not be parsed as ulong");
    }
    if (hashCodeFallback)
    {
      if (!int.TryParse(hashCodeStr, out userHashcode))
      {
        return QuickError("Parameter 'hashcode_fallback' was 'true' but the hashcode argument was missing or not an int");
      }
    }

    // Attempt to dump the object and remote type
    ObjectDump od = null;
    int retries = 10;
    while (--retries > 0)
    {
      try
      {
        (object instance, ulong pinnedAddress) = GetObject(objAddr, pinningRequested, typeName, hashCodeFallback ? userHashcode : null);
        od = ObjectDumpFactory.Create(instance, objAddr, pinnedAddress);
        break;
      }
      catch (Exception e)
      {
        if (retries == 0)
          return QuickError("Failed to retrieve the remote object. Error: " + e.Message);
        Thread.Sleep(100);
      }
    }
    if (od == null)
      return QuickError("Could not retrieve the remote object (used all retries).");

    return JsonConvert.SerializeObject(od);
  }

  private string MakeCreateObjectResponse(HttpListenerRequest arg)
  {
    Logger.Debug("[Diver] Got /create_object request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    var request = JsonConvert.DeserializeObject<CtorInvocationRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }


    Type t = null;
    lock (_clrMdLock)
    {
      t = _unifiedAppDomain.ResolveType(request.TypeFullName);
    }
    if (t == null)
    {
      return QuickError("Failed to resolve type");
    }

    List<object> paramsList = new();
    if (request.Parameters.Any())
    {
      Logger.Debug($"[Diver] Ctor'ing with parameters. Count: {request.Parameters.Count}");
      paramsList = request.Parameters.Select(ParseParameterObject).ToList();
    }
    else
    {
      // No parameters.
      Logger.Debug("[Diver] Ctor'ing without parameters");
    }

    object createdObject = null;
    try
    {
      object[] paramsArray = paramsList.ToArray();
      createdObject = Activator.CreateInstance(t, paramsArray);
    }
    catch
    {
      Debugger.Launch();
      return QuickError("Activator.CreateInstance threw an exception");
    }

    if (createdObject == null)
    {
      return QuickError("Activator.CreateInstance returned null");
    }

    // Need to return the results. If it's primitive we'll encode it
    // If it's non-primitive we pin it and send the address.
    ObjectOrRemoteAddress res;
    ulong pinAddr;
    if (createdObject.GetType().IsPrimitiveEtc())
    {
      // TODO: Something else?
      pinAddr = 0xeeffeeff;
      res = ObjectOrRemoteAddress.FromObj(createdObject);
    }
    else
    {
      // Pinning results
      pinAddr = _freezer.Pin(createdObject);
      res = ObjectOrRemoteAddress.FromToken(pinAddr, createdObject.GetType().FullName);
    }


    InvocationResults invoRes = new()
    {
      ReturnedObjectOrAddress = res,
      VoidReturnType = false
    };
    return JsonConvert.SerializeObject(invoRes);

  }

  private string MakeInvokeResponse(HttpListenerRequest arg)
  {
    Logger.Debug("[Diver] Got /Invoke request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    TextReader textReader = new StringReader(body);
    var request = JsonConvert.DeserializeObject<InvocationRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    // Need to figure target instance and the target type.
    // In case of a static call the target instance stays null.
    object instance = null;
    Type dumpedObjType;
    if (request.ObjAddress == 0)
    {
      //
      // Null target - static call
      //

      lock (_clrMdLock)
      {
        dumpedObjType = _unifiedAppDomain.ResolveType(request.TypeFullName);
      }
    }
    else
    {
      //
      // Non-null target object address. Non-static call
      //

      // Check if we have this objects in our pinned pool
      if (_freezer.TryGetPinnedObject(request.ObjAddress, out instance))
      {
        // Found pinned object!
        dumpedObjType = instance.GetType();
      }
      else
      {
        // Object not pinned, try get it the hard way
        ClrObject clrObj;
        lock (_clrMdLock)
        {
          clrObj = _runtime.Heap.GetObject(request.ObjAddress);
        }
        if (clrObj.Type == null)
        {
          return QuickError("'address' points at an invalid address");
        }

        // Make sure it's still in place
        RefreshRuntime();
        lock (_clrMdLock)
        {
          clrObj = _runtime.Heap.GetObject(request.ObjAddress);
        }
        if (clrObj.Type == null)
        {
          return
            QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
        }

        ulong mt = clrObj.Type.MethodTable;
        dumpedObjType = _unifiedAppDomain.ResolveType(clrObj.Type.Name);
        try
        {
          instance = _converter.ConvertFromIntPtr(clrObj.Address, mt);
        }
        catch (Exception)
        {
          return
            QuickError("Couldn't get handle to requested object. It could be because the Method Table mismatched or a GC collection happened.");
        }
      }
    }


    //
    // We have our target and it's type. No look for a matching overload for the
    // function to invoke.
    //
    List<object> paramsList = new();
    if (request.Parameters.Any())
    {
      Logger.Debug($"[Diver] Invoking with parameters. Count: {request.Parameters.Count}");
      paramsList = request.Parameters.Select(ParseParameterObject).ToList();
    }
    else
    {
      // No parameters.
      Logger.Debug("[Diver] Invoking without parameters");
    }

    // Infer parameter types from received parameters.
    // Note that for 'null' arguments we don't know the type so we use a "Wild Card" type
    Type[] argumentTypes = paramsList.Select(p => p?.GetType() ?? new WildCardType()).ToArray();

    // Get types of generic arguments <T1,T2, ...>
    Type[] genericArgumentTypes = request.GenericArgsTypeFullNames.Select(typeFullName => _unifiedAppDomain.ResolveType(typeFullName)).ToArray();

    // Search the method with the matching signature
    var method = dumpedObjType.GetMethodRecursive(request.MethodName, genericArgumentTypes, argumentTypes);
    if (method == null)
    {
      Debugger.Launch();
      Logger.Debug($"[Diver] Failed to Resolved method :/");
      return QuickError("Couldn't find method in type.");
    }

    string argsSummary = string.Join(", ", argumentTypes.Select(arg => arg.Name));
    Logger.Debug($"[Diver] Resolved method: {method.Name}({argsSummary}), Containing Type: {method.DeclaringType}");

    object results = null;
    try
    {
      argsSummary = string.Join(", ", paramsList.Select(param => param?.ToString() ?? "null"));
      if (string.IsNullOrEmpty(argsSummary))
        argsSummary = "No Arguments";
      Logger.Debug($"[Diver] Invoking {method.Name} with those args (Count: {paramsList.Count}): `{argsSummary}`");
      results = method.Invoke(instance, paramsList.ToArray());
    }
    catch (Exception e)
    {
      return QuickError($"Invocation caused exception: {e}");
    }

    InvocationResults invocResults;
    if (method.ReturnType == typeof(void))
    {
      // Not expecting results.
      invocResults = new InvocationResults() { VoidReturnType = true };
    }
    else
    {
      if (results == null)
      {
        // Got back a null...
        invocResults = new InvocationResults()
        {
          VoidReturnType = false,
          ReturnedObjectOrAddress = ObjectOrRemoteAddress.Null
        };
      }
      else
      {
        // Need to return the results. If it's primitive we'll encode it
        // If it's non-primitive we pin it and send the address.
        ObjectOrRemoteAddress returnValue;
        if (results.GetType().IsPrimitiveEtc())
        {
          returnValue = ObjectOrRemoteAddress.FromObj(results);
        }
        else
        {
          // Pinning results
          ulong resultsAddress = _freezer.Pin(results);
          Type resultsType = results.GetType();
          returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
        }


        invocResults = new InvocationResults()
        {
          VoidReturnType = false,
          ReturnedObjectOrAddress = returnValue
        };
      }
    }
    return JsonConvert.SerializeObject(invocResults);
  }
  private string MakeGetFieldResponse(HttpListenerRequest arg)
  {
    Logger.Debug("[Diver] Got /get_field request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    TextReader textReader = new StringReader(body);
    FieldSetRequest request = JsonConvert.DeserializeObject<FieldSetRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    // Need to figure target instance and the target type.
    // In case of a static call the target instance stays null.
    Type dumpedObjType;
    object results;
    if (request.ObjAddress == 0)
    {
      // Null Target -- Getting a Static field
      lock (_clrMdLock)
      {
        dumpedObjType = _unifiedAppDomain.ResolveType(request.TypeFullName);
      }
      FieldInfo staticFieldInfo = dumpedObjType.GetField(request.FieldName);
      if (!staticFieldInfo.IsStatic)
      {
        return QuickError("Trying to get field with a null target bu the field was not a static one");
      }

      results = staticFieldInfo.GetValue(null);
    }
    else
    {
      object instance;
      // Check if we have this objects in our pinned pool
      if (_freezer.TryGetPinnedObject(request.ObjAddress, out instance))
      {
        // Found pinned object!
        dumpedObjType = instance.GetType();
      }
      else
      {
        return QuickError("Can't get field of a unpinned objects");
      }

      // Search the method with the matching signature
      var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
      if (fieldInfo == null)
      {
        Debugger.Launch();
        Logger.Debug($"[Diver] Failed to Resolved field :/");
        return QuickError("Couldn't find field in type.");
      }

      Logger.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

      try
      {
        results = fieldInfo.GetValue(instance);
      }
      catch (Exception e)
      {
        return QuickError($"Invocation caused exception: {e}");
      }
    }


    // Return the value we just set to the field to the caller...
    InvocationResults invocResults;
    {
      ObjectOrRemoteAddress returnValue;
      if (results.GetType().IsPrimitiveEtc())
      {
        returnValue = ObjectOrRemoteAddress.FromObj(results);
      }
      else
      {
        // Pinning results
        ulong resultsAddress = _freezer.Pin(results);
        Type resultsType = results.GetType();
        returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
      }

      invocResults = new InvocationResults()
      {
        VoidReturnType = false,
        ReturnedObjectOrAddress = returnValue
      };
    }
    return JsonConvert.SerializeObject(invocResults);

  }
  private string MakeSetFieldResponse(HttpListenerRequest arg)
  {
    Logger.Debug("[Diver] Got /set_field request!");
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
    {
      return QuickError("Missing body");
    }

    var request = JsonConvert.DeserializeObject<FieldSetRequest>(body);
    if (request == null)
    {
      return QuickError("Failed to deserialize body");
    }

    Type dumpedObjType;
    if (request.ObjAddress == 0)
    {
      return QuickError("Can't set field of a null target");
    }


    // Need to figure target instance and the target type.
    // In case of a static call the target instance stays null.
    object instance;
    // Check if we have this objects in our pinned pool
    if (_freezer.TryGetPinnedObject(request.ObjAddress, out instance))
    {
      // Found pinned object!
      dumpedObjType = instance.GetType();
    }
    else
    {
      // Object not pinned, try get it the hard way
      ClrObject clrObj = default;
      lock (_clrMdLock)
      {
        clrObj = _runtime.Heap.GetObject(request.ObjAddress);
        if (clrObj.Type == null)
        {
          return QuickError("'address' points at an invalid address");
        }

        // Make sure it's still in place
        RefreshRuntime();
        clrObj = _runtime.Heap.GetObject(request.ObjAddress);
      }
      if (clrObj.Type == null)
      {
        return
          QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
      }

      ulong mt = clrObj.Type.MethodTable;
      dumpedObjType = _unifiedAppDomain.ResolveType(clrObj.Type.Name);
      try
      {
        instance = _converter.ConvertFromIntPtr(clrObj.Address, mt);
      }
      catch (Exception)
      {
        return
          QuickError("Couldn't get handle to requested object. It could be because the Method Table or a GC collection happened.");
      }
    }

    // Search the method with the matching signature
    var fieldInfo = dumpedObjType.GetFieldRecursive(request.FieldName);
    if (fieldInfo == null)
    {
      Debugger.Launch();
      Logger.Debug($"[Diver] Failed to Resolved field :/");
      return QuickError("Couldn't find field in type.");
    }
    Logger.Debug($"[Diver] Resolved field: {fieldInfo.Name}, Containing Type: {fieldInfo.DeclaringType}");

    object results = null;
    try
    {
      object value = ParseParameterObject(request.Value);
      fieldInfo.SetValue(instance, value);
      // Reading back value to return to caller. This is expected C# behaviour:
      // int x = this.field_y = 3; // Makes both x and field_y equal 3.
      results = fieldInfo.GetValue(instance);
    }
    catch (Exception e)
    {
      return QuickError($"Invocation caused exception: {e}");
    }


    // Return the value we just set to the field to the caller...
    InvocationResults invocResults;
    {
      ObjectOrRemoteAddress returnValue;
      if (results.GetType().IsPrimitiveEtc())
      {
        returnValue = ObjectOrRemoteAddress.FromObj(results);
      }
      else
      {
        // Pinning results
        ulong resultsAddress = _freezer.Pin(results);
        Type resultsType = results.GetType();
        returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
      }

      invocResults = new InvocationResults()
      {
        VoidReturnType = false,
        ReturnedObjectOrAddress = returnValue
      };
    }
    return JsonConvert.SerializeObject(invocResults);
  }
  private string MakeArrayItemResponse(HttpListenerRequest arg)
  {
    string body = null;
    using (StreamReader sr = new(arg.InputStream))
    {
      body = sr.ReadToEnd();
    }

    if (string.IsNullOrEmpty(body))
      return QuickError("Missing body");

    var request = JsonConvert.DeserializeObject<IndexedItemAccessRequest>(body);
    if (request == null)
      return QuickError("Failed to deserialize body");

    ulong objAddr = request.CollectionAddress;
    object index = ParseParameterObject(request.Index);
    bool pinningRequested = request.PinRequest;

    // Check if we have this objects in our pinned pool
    if (!_freezer.TryGetPinnedObject(objAddr, out object pinnedObj))
    {
      // Object not pinned, try get it the hard way
      return QuickError("Object at given address wasn't pinned (context: ArrayItemAccess)");
    }

    object item = null;
    if (pinnedObj.GetType().IsArray)
    {
      Array asArray = (Array)pinnedObj;
      if (index is not int intIndex)
        return QuickError("Tried to access an Array with a non-int index");

      int length = asArray.Length;
      if (intIndex >= length)
        return QuickError("Index out of range");

      item = asArray.GetValue(intIndex);
    }
    else if (pinnedObj is IList asList)
    {
      object[] asArray = asList?.Cast<object>().ToArray();
      if (asArray == null)
        return QuickError("Object at given address seemed to be an IList but failed to convert to array");

      if (index is not int intIndex)
        return QuickError("Tried to access an IList with a non-int index");

      int length = asArray.Length;
      if (intIndex >= length)
        return QuickError("Index out of range");

      // Get the item
      item = asArray[intIndex];
    }
    else if (pinnedObj is IDictionary dict)
    {
      Logger.Debug("[Diver] Array access: Object is an IDICTIONARY!");
      item = dict[index];
    }
    else if (pinnedObj is IEnumerable enumerable)
    {
      // Last result - generic IEnumerables can be enumerated into arrays.
      // BEWARE: This could lead to "runining" of the IEnumerable if it's a not "resetable"
      object[] asArray = enumerable?.Cast<object>().ToArray();
      if (asArray == null)
        return QuickError("Object at given address seemed to be an IEnumerable but failed to convert to array");

      if (index is not int intIndex)
        return QuickError("Tried to access an IEnumerable (which isn't an Array, IList or IDictionary) with a non-int index");

      int length = asArray.Length;
      if (intIndex >= length)
        return QuickError("Index out of range");

      // Get the item
      item = asArray[intIndex];
    }
    else
    {
      Logger.Debug("[Diver] Array access: Object isn't an Array, IList, IDictionary or IEnumerable");
      return QuickError("Object isn't an Array, IList, IDictionary or IEnumerable");
    }

    ObjectOrRemoteAddress res;
    if (item == null)
    {
      res = ObjectOrRemoteAddress.Null;
    }
    else if (item.GetType().IsPrimitiveEtc())
    {
      // TODO: Something else?
      res = ObjectOrRemoteAddress.FromObj(item);
    }
    else
    {
      // Non-primitive results must be pinned before returning their remote address
      // TODO: If a RemoteObject is not created for this object later and the item is not automaticlly unfreezed it might leak.
      if (!_freezer.TryGetPinningAddress(item, out ulong addr))
      {
        // Item not pinned yet, let's do it.
        addr = _freezer.Pin(item);
      }

      res = ObjectOrRemoteAddress.FromToken(addr, item.GetType().FullName);
    }


    InvocationResults invokeRes = new()
    {
      VoidReturnType = false,
      ReturnedObjectOrAddress = res
    };


    return JsonConvert.SerializeObject(invokeRes);
  }

  private string MakeUnpinResponse(HttpListenerRequest arg)
  {
    string objAddrStr = arg.QueryString.Get("address");
    if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
    {
      return QuickError("Missing parameter 'address'");
    }
    Logger.Debug($"[Diver][Debug](Unpin) objAddrStr={objAddr:X16}");

    // Remove if we have this object in our pinned pool, otherwise ignore.
    if (_freezer.TryGetPinnedObject(objAddr, out _))
      _freezer.Unpin(objAddr);

    return "{\"status\":\"OK\"}";
  }

  private string MakeDieResponse(HttpListenerRequest req)
  {
    Logger.Debug("[Diver] Die command received");
    bool forceKill = req.QueryString.Get("force")?.ToUpper() == "TRUE";
    lock (_registeredPidsLock)
    {
      if (_registeredPids.Count > 0 && !forceKill)
      {
        Logger.Debug("[Diver] Die command failed - More clients exist.");
        return "{\"status\":\"Error more clients remaining. You can use the force=true argument to ignore this check.\"}";
      }
    }

    Logger.Debug("[Diver] Die command accepted.");
    _stayAlive.Reset();
    return "{\"status\":\"Goodbye\"}";
  }

  // IDisposable
  public void Dispose()
  {
    DisposeRuntime();
  }
}
