/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using MTGOSDK.Core.Compiler;
using MTGOSDK.Core.Compiler.Extensions;
using MTGOSDK.Core.Compiler.Snapshot;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;

using MTGOSDK.Win32.API;


namespace ScubaDiver;

public class Diver : IDisposable
{
  // Runtime analysis and exploration fields
  private SnapshotRuntime _runtime;

  // Clients Tracking
  public object _registeredPidsLock = new();
  public List<int> _registeredPids = new();

  // HTTP Responses fields
  private readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

  // Callbacks Endpoint of the Controller process
  private bool _monitorEndpoints = true;
  private int _nextAvailableCallbackToken;
  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;

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
  }

  public void Start(ushort listenPort)
  {
    // Start the ClrMD runtime
    _runtime = new SnapshotRuntime();

    // Start session
    HttpListener listener = new();
    string listeningUrl = $"http://127.0.0.1:{listenPort}/";
    listener.Prefixes.Add(listeningUrl);
    // Set timeout
    var manager = listener.TimeoutManager;
    manager.IdleConnection = TimeSpan.FromSeconds(5);
    listener.Start();
    Logger.Debug($"[Diver] Listening on {listeningUrl}...");

    Task endpointsMonitor = Task.Run(CallbacksEndpointsMonitor);
    Dispatcher(listener);
    Logger.Debug("[Diver] Stopping Callback Endpoints Monitor");
    _monitorEndpoints = false;
    try { endpointsMonitor.Wait(); } catch { }

    Logger.Debug("[Diver] Closing listener");
    listener.Stop();
    listener.Close();
    Logger.Debug("[Diver] Closing ClrMD runtime and snapshot");

    Logger.Debug("[Diver] Unpinning objects");
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

  public string QuickError(string error, string stackTrace = null)
  {
    if (stackTrace == null)
    {
      stackTrace = (new StackTrace(true)).ToString();
    }
    DiverError errResults = new(error, stackTrace);
    return JsonConvert.SerializeObject(errResults);
  }

  #region HTTP Dispatching
  private void HandleDispatchedRequest(HttpListenerContext requestContext)
  {
    HttpListenerRequest request = requestContext.Request;

    var response = requestContext.Response;
    string body;
    Logger.Debug($"--- {request.Url.AbsolutePath} ---");
    if (_responseBodyCreators.TryGetValue(request.Url.AbsolutePath, out var respBodyGenerator))
    {
      try
      {
        body = respBodyGenerator(request);
      }
      catch (Exception ex)
      {
        Logger.Debug("[Diver] Exception in handler: " + ex.ToString());
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
          // Async operation started! We can move on to next request
          break;
        }
        else
        {
          // Async event still awaiting new HTTP requests.
          // It's a good time to check if we were signaled to die
          if (!_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
          {
            // Time to die.
            // Leaving the inner loop will get us to the outer loop where _stayAlive is checked (again)
            // and then it that loop will stop as well.
            break;
          }
          else
          {
            // No signal of die command. We can continue waiting
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
    // Extract the names of all available modules from the current AppDomain.
    List<string> modules = new();
    string currentDomain = AppDomain.CurrentDomain.FriendlyName;
    lock (_runtime.clrLock)
    {
      ClrAppDomain clrAppDomain = _runtime.GetClrAppDomains()
        .FirstOrDefault(ad => ad.Name == currentDomain);
      modules = clrAppDomain.Modules
        .Select(m => Path.GetFileNameWithoutExtension(m.Name))
        .Where(m => !string.IsNullOrWhiteSpace(m))
        .ToList();
    }

    DomainDump domainDump = new(currentDomain, modules);
    // DomainDump domainDump = new() { Name = currentDomain, Modules = modules };
    return JsonConvert.SerializeObject(domainDump);
  }

  private string MakeTypesResponse(HttpListenerRequest req)
  {
    string assembly = req.QueryString.Get("assembly");
    Assembly matchingAssembly = _runtime.ResolveAssembly(assembly);
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
    Type resolvedType = _runtime.ResolveType(type, assembly);

    if (resolvedType != null)
    {
      TypeDump recursiveTypeDump = TypeDump.ParseType(resolvedType);
      return JsonConvert.SerializeObject(recursiveTypeDump);
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

    // Default filter - Find all exact matches based on the filter type.
    Predicate<string> matchesFilter = (string typeName) => typeName == filter;

    (bool anyErrors, List<HeapDump.HeapObject> objects) = _runtime.GetHeapObjects(
      matchesFilter,
      dumpHashcodes
    );
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
    if (!_runtime.TryGetPinnedObject(objAddr, out object target))
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
      else
      {
        ulong addr = _runtime.PinObject(parameter);
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
        (object instance, ulong pinnedAddress) = _runtime.GetHeapObject(
          objAddr,
          pinningRequested,
          typeName,
          hashCodeFallback ? userHashcode : null
        );
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


    Type t = _runtime.ResolveType(request.TypeFullName);
    if (t == null)
    {
      return QuickError("Failed to resolve type");
    }

    List<object> paramsList = new();
    if (request.Parameters.Any())
    {
      Logger.Debug($"[Diver] Ctor'ing with parameters. Count: {request.Parameters.Count}");
      paramsList = request.Parameters.Select(_runtime.ParseParameterObject).ToList();
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
      pinAddr = _runtime.PinObject(createdObject);
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

      dumpedObjType = _runtime.ResolveType(request.TypeFullName);
    }
    else
    {
      //
      // Non-null target object address. Non-static call
      //

      // Check if we have this objects in our pinned pool
      if (_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
      {
        // Found pinned object!
        dumpedObjType = instance.GetType();
      }
      else
      {
        // Object not pinned, try get it the hard way
        ClrObject clrObj = default;
        lock (_runtime.clrLock)
        {
          clrObj = _runtime.GetClrObject(request.ObjAddress);
          if (clrObj.Type == null)
          {
            return QuickError("'address' points at an invalid address");
          }

          // Make sure it's still in place
          _runtime.RefreshRuntime();
          clrObj = _runtime.GetClrObject(request.ObjAddress);
        }
        if (clrObj.Type == null)
        {
          return
            QuickError("Object moved since last refresh. 'address' now points at an invalid address.");
        }

        ulong mt = clrObj.Type.MethodTable;
        dumpedObjType = _runtime.ResolveType(clrObj.Type.Name);
        try
        {
          instance = _runtime.Compile(clrObj.Address, mt);
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
      paramsList = request.Parameters.Select(_runtime.ParseParameterObject).ToList();
    }
    else
    {
      // No parameters.
      Logger.Debug("[Diver] Invoking without parameters");
    }

    // Infer parameter types from received parameters.
    // Note that for 'null' arguments we don't know the type so we use a "Wild Card" type
    Type[] argumentTypes = paramsList.Select(p => p?.GetType() ?? new TypeStub()).ToArray();

    // Get types of generic arguments <T1,T2, ...>
    Type[] genericArgumentTypes = request.GenericArgsTypeFullNames.Select(typeFullName => _runtime.ResolveType(typeFullName)).ToArray();

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
          ulong resultsAddress = _runtime.PinObject(results);
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
      dumpedObjType = _runtime.ResolveType(request.TypeFullName);
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
      if (_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
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
        ulong resultsAddress = _runtime.PinObject(results);
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
    if (_runtime.TryGetPinnedObject(request.ObjAddress, out instance))
    {
      // Found pinned object!
      dumpedObjType = instance.GetType();
    }
    else
    {
      // Object not pinned, try get it the hard way
      ClrObject clrObj = default;
      lock (_runtime.clrLock)
      {
        clrObj = _runtime.GetClrObject(request.ObjAddress);
        if (clrObj.Type == null)
        {
          return QuickError($"The invalid address for '${request.TypeFullName}'.");
        }

        // Make sure it's still in place
        _runtime.RefreshRuntime();
        clrObj = _runtime.GetClrObject(request.ObjAddress);
      }
      if (clrObj.Type == null)
      {
        return
          QuickError($"The address for '${request.TypeFullName}' moved since last refresh.");
      }

      ulong mt = clrObj.Type.MethodTable;
      dumpedObjType = _runtime.ResolveType(clrObj.Type.Name);
      try
      {
        instance = _runtime.Compile(clrObj.Address, mt);
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
      object value = _runtime.ParseParameterObject(request.Value);
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
        ulong resultsAddress = _runtime.PinObject(results);
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
    object index = _runtime.ParseParameterObject(request.Index);
    bool pinningRequested = request.PinRequest;

    // Check if we have this objects in our pinned pool
    if (!_runtime.TryGetPinnedObject(objAddr, out object pinnedObj))
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
      ulong addr = _runtime.PinObject(item);

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
    _runtime.UnpinObject(objAddr);

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
    _runtime?.Dispose();
  }
}
