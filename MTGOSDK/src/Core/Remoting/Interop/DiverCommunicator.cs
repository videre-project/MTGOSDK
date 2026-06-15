/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;

using MessagePack;

using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// TCP-based communicator with the Diver in a remote process.
/// </summary>
public class DiverCommunicator : IDisposable
{
  public sealed class CallbackMapDiagnostics
  {
    public int EventTokenEntries { get; set; }
    public int EventCallbackEntries { get; set; }
    public int HookTokenEntries { get; set; }
    public int HookCallbackEntries { get; set; }
  }

  private readonly TcpCommunicator _tcp;
  private int? _process_id = null;
  private Exception? _lastError;

  public Exception? LastError => _lastError;

  // Callback handling
  private readonly ConcurrentDictionary<int, LocalEventCallback> _tokensToEventHandlers = new();
  private readonly ConcurrentDictionary<LocalEventCallback, int> _eventHandlersToToken = new();
  private readonly ConcurrentDictionary<int, LocalHookCallback> _tokensToHookCallbacks = new();
  private readonly ConcurrentDictionary<LocalHookCallback, int> _hookCallbacksToTokens = new();

  private static readonly AsyncLocal<bool> s_forceUIThread = new();
  private volatile bool _isDisposed = false;

  //
  // IPC diagnostics
  //

  internal sealed class EndpointMetrics
  {
    public long Count;
    public long TotalTicks;
    public long LastTicks;
  }

  private static readonly ConcurrentDictionary<string, EndpointMetrics>
    s_ipcMetrics = new();
  private static long s_totalRequests;
  private static long s_callbacksReceived;
  private static double s_lastCallbackLatencyMs;
  private static double s_avgCallbackLatencyMs;
  private static double s_peakCallbackLatencyMs;
  private static long s_peakResetTicks = Stopwatch.GetTimestamp();

  public static long TotalRequests => Interlocked.Read(ref s_totalRequests);
  public static long CallbacksReceived =>
    Interlocked.Read(ref s_callbacksReceived);
  public static double LastCallbackLatencyMs => s_lastCallbackLatencyMs;
  public static double AvgCallbackLatencyMs => s_avgCallbackLatencyMs;
  public static double PeakCallbackLatencyMs => s_peakCallbackLatencyMs;

  public static IReadOnlyDictionary<string, (long Count, double AvgMs, double LastMs)>
    GetIpcMetrics()
  {
    var result = new Dictionary<string, (long, double, double)>();
    foreach (var kvp in s_ipcMetrics)
    {
      long count = Interlocked.Read(ref kvp.Value.Count);
      if (count == 0) continue;

      double freq = Stopwatch.Frequency;
      result[kvp.Key] = (
        count,
        (Interlocked.Read(ref kvp.Value.TotalTicks) / (double)count)
          / freq * 1000.0,
        Interlocked.Read(ref kvp.Value.LastTicks) / freq * 1000.0
      );
    }
    return result;
  }

  public CallbackMapDiagnostics GetCallbackMapDiagnostics() => new()
  {
    EventTokenEntries = _tokensToEventHandlers.Count,
    EventCallbackEntries = _eventHandlersToToken.Count,
    HookTokenEntries = _tokensToHookCallbacks.Count,
    HookCallbackEntries = _hookCallbacksToTokens.Count,
  };

  public static bool ForceUIThread
  {
    get => s_forceUIThread.Value;
    set => s_forceUIThread.Value = value;
  }

  public static IDisposable BeginUIThreadScope() => new UIThreadScope();

  private sealed class UIThreadScope : IDisposable
  {
    private readonly bool _previousValue;
    private readonly bool _suppressed;
    private readonly System.Threading.AsyncFlowControl _flowControl;

    public UIThreadScope()
    {
      _previousValue = ForceUIThread;
      
      // Suppress ExecutionContext flow to prevent AsyncLocal bleeding
      // to concurrent/parallel operations
      if (!System.Threading.ExecutionContext.IsFlowSuppressed())
      {
        _flowControl = System.Threading.ExecutionContext.SuppressFlow();
        _suppressed = true;
      }
      
      ForceUIThread = true;
    }

    public void Dispose()
    {
      ForceUIThread = _previousValue;
      if (_suppressed)
        _flowControl.Undo();
    }
  }

  public bool IsConnected => _tcp.IsConnected;

  /// <summary>
  /// The number of IPC requests currently awaiting a response.
  /// </summary>
  public int PendingRequestCount => _tcp.PendingRequestCount;

  public bool Disconnect()
  {
    if (!IsConnected) return false;
    return UnregisterClient(_process_id);
  }

  public void Cancel() => _tcp?.Dispose();

  public DiverCommunicator(
    string hostname,
    int diverPort,
    CancellationTokenSource cts = null)
  {
    _tcp = new TcpCommunicator(hostname, diverPort, cts);

    // Register callback handler for incoming events/hooks from Diver
    _tcp.SetCallbackHandler(HandleCallback);

    RemoteClient.Disposed += (s, e) =>
    {
      _isDisposed = true;
      SyncThread.Enqueue(() =>
      {
        Cancel();
        _process_id = null;
      });
    };
  }

  /// <summary>
  /// Connect to the Diver via TCP.
  /// </summary>
  public async Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    await _tcp.ConnectAsync(cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Handles callbacks (events/hooks) received from the Diver.
  /// </summary>
  private void HandleCallback(string endpoint, byte[] body)
  {
    if (endpoint == "/invoke_callback")
    {
      var request = MessagePackSerializer.Deserialize<CallbackInvocationRequest>(body);
      Interlocked.Increment(ref s_callbacksReceived);

      // Measure end-to-end callback delivery latency.
      // Normalize to UTC before comparing — MessagePack serializes DateTime
      // as UTC ticks, so the deserialized Kind may differ from DateTime.Now.
      double latencyMs = (DateTime.UtcNow - request.Timestamp.ToUniversalTime())
        .TotalMilliseconds;
      s_lastCallbackLatencyMs = latencyMs;
      s_avgCallbackLatencyMs = s_avgCallbackLatencyMs == 0
        ? latencyMs
        : s_avgCallbackLatencyMs * 0.9 + latencyMs * 0.1;

      // Rolling 5-second peak: reset if window expired, otherwise keep max
      long now = Stopwatch.GetTimestamp();
      if ((now - s_peakResetTicks) / (double)Stopwatch.Frequency > 5.0)
      {
        s_peakCallbackLatencyMs = latencyMs;
        s_peakResetTicks = now;
      }
      else if (latencyMs > s_peakCallbackLatencyMs)
      {
        s_peakCallbackLatencyMs = latencyMs;
      }

      // Set the timestamp for the sender
      if (request.Parameters.Count > 0)
        request.Parameters[0].Timestamp = request.Timestamp;

      if (_tokensToEventHandlers.TryGetValue(request.Token, out LocalEventCallback callback))
      {
        // Set timestamps for event args
        if (request.Parameters.Count > 1)
          request.Parameters[1].Timestamp = request.Timestamp;

        callback([.. request.Parameters]);
      }
      else if (_tokensToHookCallbacks.TryGetValue(request.Token, out LocalHookCallback hook))
      {
        hook(new HookContext(request.Timestamp),
             request.Parameters.FirstOrDefault(),
             [.. request.Parameters.Skip(1)]);
      }
    }
  }

  private static ReadOnlyMemory<byte> Serialize<T>(T value) =>
    MessagePackSerializer.Serialize(value);

  /// <summary>
  /// Ensures the TCP connection is established.
  /// </summary>
  private void EnsureConnected()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(DiverCommunicator), "MTGO process has closed");

    if (!_tcp.IsConnected)
    {
      _tcp.ConnectAsync().GetAwaiter().GetResult();
    }
  }

  /// <summary>
  /// Sends a request and returns the response.
  /// </summary>
  private T SendRequest<T>(string endpoint, object request = null)
  {
    EnsureConnected();
    Interlocked.Increment(ref s_totalRequests);

    byte[] body = request != null ? MessagePackSerializer.Serialize(request) : null;
    var sw = Stopwatch.StartNew();
    try
    {
      return _tcp.SendRequestAsync<T>("/" + endpoint, body).GetAwaiter().GetResult();
    }
    finally
    {
      sw.Stop();
      var m = s_ipcMetrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
      Interlocked.Increment(ref m.Count);
      Interlocked.Add(ref m.TotalTicks, sw.ElapsedTicks);
      Interlocked.Exchange(ref m.LastTicks, sw.ElapsedTicks);
    }
  }

  /// <summary>
  /// Sends a request without expecting a response.
  /// </summary>
  private void SendRequest(string endpoint, object request = null)
  {
    EnsureConnected();
    Interlocked.Increment(ref s_totalRequests);

    byte[] body = request != null ? MessagePackSerializer.Serialize(request) : null;
    var sw = Stopwatch.StartNew();
    try
    {
      _tcp.SendRequestAsync("/" + endpoint, body).GetAwaiter().GetResult();
    }
    finally
    {
      sw.Stop();
      var m = s_ipcMetrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
      Interlocked.Increment(ref m.Count);
      Interlocked.Add(ref m.TotalTicks, sw.ElapsedTicks);
      Interlocked.Exchange(ref m.LastTicks, sw.ElapsedTicks);
    }
  }

  public HeapDump DumpHeap(string typeFilter = null, bool dumpHashcodes = true)
  {
    var request = new HeapDumpRequest
    {
      TypeFilter = typeFilter,
      DumpHashcodes = dumpHashcodes
    };
    return SendRequest<HeapDump>("heap", request);
  }

  public DomainDump DumpDomain() =>
    SendRequest<DomainDump>("domains");

  public TypesDump DumpTypes(string assembly)
  {
    var request = new TypesDumpRequest { Assembly = assembly };
    return SendRequest<TypesDump>("types", request);
  }

  public TypeDump DumpType(string type, string assembly = null)
  {
    var dumpRequest = new TypeDumpRequest
    {
      TypeFullName = type,
      Assembly = assembly
    };
    return SendRequest<TypeDump>("type", dumpRequest);
  }

  public ObjectDump DumpObject(
    ulong address,
    string typeName,
    bool pinObject = false,
    int? hashcode = null)
  {
    var request = new ObjectDumpRequest
    {
      Address = address,
      TypeName = typeName,
      PinRequest = pinObject,
      Hashcode = hashcode,
      HashcodeFallback = hashcode.HasValue
    };
    return SendRequest<ObjectDump>("object", request);
  }

  public void UnpinObject(ulong address)
  {
    var request = new UnpinRequest { Address = address };
    SendRequest("unpin", request);
  }

  public InvocationResults InvokeMethod(
    ulong targetAddr,
    string targetTypeFullName,
    string methodName,
    string[] genericArgsFullTypeNames,
    params ObjectOrRemoteAddress[] args)
  {
    var invocReq = new InvocationRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      MethodName = methodName,
      GenericArgsTypeFullNames = genericArgsFullTypeNames,
      Parameters = new List<ObjectOrRemoteAddress>(args),
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("invoke", invocReq);
  }

  public bool RegisterClient(int? process_id = null)
  {
    _process_id = process_id ?? Process.GetCurrentProcess().Id;
    _lastError = null;

    try
    {
      var request = new RegisterClientRequest { ProcessId = _process_id.Value };
      SendRequest("register_client", request);
      return true;
    }
    catch (Exception ex)
    {
      _lastError = ex;
      return false;
    }
  }

  public bool UnregisterClient(int? process_id = null)
  {
    _process_id = process_id ?? Process.GetCurrentProcess().Id;

    try
    {
      var request = new UnregisterClientRequest { ProcessId = _process_id.Value };
      SendRequest("unregister_client", request);
      return true;
    }
    catch
    {
      return false;
    }
    finally
    {
      _process_id = null;
    }
  }

  public bool CheckAliveness()
  {
    try
    {
      SendRequest("ping");
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// Queries the Diver for its current diagnostics snapshot.
  /// </summary>
  public DiverDiagnostics GetDiverDiagnostics() =>
    SendRequest<DiverDiagnostics>("diagnostics");

  /// <summary>
  /// Takes a heap snapshot, building type stats and reverse reference map.
  /// </summary>
  public HeapSnapshotResponse GetHeapSnapshot(int topN = 50) =>
    SendRequest<HeapSnapshotResponse>("heap_snapshot",
      new HeapSnapshotRequest { TopN = topN });

  /// <summary>
  /// Computes the retain chain for the largest instance of the given type
  /// using batched reverse BFS (requires a prior heap snapshot).
  /// </summary>
  public RetainChainResponse GetRetainChain(string typeName, int maxDepth = 8) =>
    SendRequest<RetainChainResponse>("retain_chain",
      new RetainChainRequest { TypeName = typeName, MaxDepth = maxDepth });

  /// <summary>
  /// Returns the largest instances of the given type (from cached snapshot).
  /// </summary>
  public TypeInstancesResponse GetTypeInstances(string typeName, int maxCount = 20) =>
    SendRequest<TypeInstancesResponse>("type_instances",
      new TypeInstancesRequest { TypeName = typeName, MaxCount = maxCount });

  /// <summary>
  /// Analyzes which static root fields hold the most retained memory in the
  /// process, via forward BFS with dominator approximation.
  /// </summary>
  public StaticHoldersResponse AnalyzeStaticHolders(int topN = 50) =>
    SendRequest<StaticHoldersResponse>("static_holders",
      new StaticHoldersRequest { TopN = topN });

  public ObjectOrRemoteAddress GetItem(ulong token, ObjectOrRemoteAddress key)
  {
    var request = new IndexedItemAccessRequest
    {
      CollectionAddress = token,
      PinRequest = true,
      Index = key,
      ForceUIThread = ForceUIThread
    };
    var result = SendRequest<InvocationResults>("get_item", request);
    return result.ReturnedObjectOrAddress;
  }

  public InvocationResults InvokeStaticMethod(
    string targetTypeFullName,
    string methodName,
    params ObjectOrRemoteAddress[] args)
    => InvokeStaticMethod(targetTypeFullName, methodName, null, args);

  public InvocationResults InvokeStaticMethod(
    string targetTypeFullName,
    string methodName,
    string[] genericArgsFullTypeNames,
    params ObjectOrRemoteAddress[] args)
    => InvokeMethod(0, targetTypeFullName, methodName, genericArgsFullTypeNames, args);

  public InvocationResults CreateObject(
    string typeFullName,
    ObjectOrRemoteAddress[] args)
  {
    var ctorInvocReq = new CtorInvocationRequest
    {
      TypeFullName = typeFullName,
      Parameters = new List<ObjectOrRemoteAddress>(args),
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("create_object", ctorInvocReq);
  }

  public InvocationResults CreateArray(string elementTypeFullName, int length)
  {
    var request = new ArrayCreationRequest
    {
      ElementTypeFullName = elementTypeFullName,
      Length = length
    };
    return SendRequest<InvocationResults>("create_array", request);
  }

  public InvocationResults CreateArray(
    string elementTypeFullName,
    List<List<ObjectOrRemoteAddress>> constructorArgs)
  {
    var request = new ArrayCreationRequest
    {
      ElementTypeFullName = elementTypeFullName,
      ConstructorArgs = constructorArgs
    };
    return SendRequest<InvocationResults>("create_array", request);
  }

  public InvocationResults SetField(
    ulong targetAddr,
    string targetTypeFullName,
    string fieldName,
    ObjectOrRemoteAddress newValue)
  {
    var invocReq = new FieldSetRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      FieldName = fieldName,
      Value = newValue,
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("set_field", invocReq);
  }

  public InvocationResults GetField(
    ulong targetAddr,
    string targetTypeFullName,
    string fieldName)
  {
    var invocReq = new FieldGetRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      FieldName = fieldName,
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("get_field", invocReq);
  }

  /// <summary>
  /// Fetches multiple property values in a single IPC call.
  /// </summary>
  /// <param name="targetAddr">Address of the pinned remote object.</param>
  /// <param name="targetTypeFullName">Full type name of the object.</param>
  /// <param name="pathsDelimited">Pipe-delimited property paths (e.g., "Name|Id|Rarity.Name").</param>
  /// <returns>Response containing values dictionary and types dictionary.</returns>
  public Interactions.Object.BatchMembersResponse GetBatchMembers(
    ulong targetAddr,
    string targetTypeFullName,
    string pathsDelimited)
  {
    var request = new Interactions.Object.BatchMembersRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      PathsDelimited = pathsDelimited
    };
    return SendRequest<Interactions.Object.BatchMembersResponse>("batch_members", request);
  }

  /// <summary>
  /// Fetches properties for all items in a collection in a single IPC call.
  /// </summary>
  /// <param name="collectionAddr">Address of the IEnumerable collection.</param>
  /// <param name="collectionTypeName">Type name of the collection.</param>
  /// <param name="pathsDelimited">Pipe-delimited property paths.</param>
  /// <param name="maxItems">Maximum items to process (0 = no limit).</param>
  /// <returns>Response containing all items' property values.</returns>
  public Interactions.Object.BatchCollectionResponse GetBatchCollectionMembers(
    ulong collectionAddr,
    string collectionTypeName,
    string pathsDelimited,
    int maxItems = 0)
  {
    var request = new Interactions.Object.BatchCollectionRequest
    {
      CollectionAddress = collectionAddr,
      CollectionTypeName = collectionTypeName,
      PathsDelimited = pathsDelimited,
      MaxItems = maxItems
    };
    return SendRequest<Interactions.Object.BatchCollectionResponse>("batch_collection", request);
  }

  /// <summary>
  /// Async variant of <see cref="GetBatchCollectionMembers"/> that does not
  /// block a thread pool thread while waiting for the IPC response.
  /// </summary>
  public Task<Interactions.Object.BatchCollectionResponse> GetBatchCollectionMembersAsync(
    ulong collectionAddr,
    string collectionTypeName,
    string pathsDelimited,
    int maxItems = 0)
  {
    EnsureConnected();
    var request = new Interactions.Object.BatchCollectionRequest
    {
      CollectionAddress = collectionAddr,
      CollectionTypeName = collectionTypeName,
      PathsDelimited = pathsDelimited,
      MaxItems = maxItems
    };
    byte[] body = MessagePackSerializer.Serialize(request);
    return _tcp.SendRequestAsync<Interactions.Object.BatchCollectionResponse>(
      "/batch_collection", body);
  }

  public void EventSubscribe(
    ulong targetAddr,
    string eventName,
    LocalEventCallback callback)
  {
    var request = new EventSubscriptionRequest
    {
      Address = targetAddr,
      EventName = eventName
    };
    var regRes = SendRequest<EventRegistrationResults>("event_subscribe", request);
    _tokensToEventHandlers[regRes.Token] = callback;
    _eventHandlersToToken[callback] = regRes.Token;
  }

  public void EventUnsubscribe(LocalEventCallback callback)
  {
    if (!TryRemoveEventCallback(callback, out int token))
      throw new Exception("EventUnsubscribe: callback not found");

    var request = new EventUnsubscriptionRequest { Token = token };
    SendRequest("event_unsubscribe", request);
  }

  private bool TryRemoveEventCallback(LocalEventCallback callback, out int token)
  {
    if (_eventHandlersToToken.TryRemove(callback, out token))
    {
      _tokensToEventHandlers.TryRemove(token, out _);
      return true;
    }

    foreach (var entry in _tokensToEventHandlers)
    {
      if (entry.Value != callback)
        continue;

      if (_tokensToEventHandlers.TryRemove(entry.Key, out _))
      {
        token = entry.Key;
        _eventHandlersToToken.TryRemove(callback, out _);
        return true;
      }
    }

    return false;
  }

  public bool HookMethod(
    string type,
    string methodName,
    HarmonyPatchPosition pos,
    LocalHookCallback callback,
    List<string> parametersTypeFullNames = null)
  {
    var req = new HookSubscriptionRequest
    {
      TypeFullName = type,
      MethodName = methodName,
      HookPosition = pos.ToString(),
      ParametersTypeFullNames = parametersTypeFullNames
    };

    var regRes = SendRequest<EventRegistrationResults>("hook_method", req);
    _tokensToHookCallbacks[regRes.Token] = callback;
    _hookCallbacksToTokens[callback] = regRes.Token;
    return true;
  }

  public void UnhookMethod(LocalHookCallback callback)
  {
    if (!TryRemoveHookCallback(callback, out int token))
      throw new Exception("UnhookMethod: callback not found");

    var request = new HookUnsubscriptionRequest { Token = token };
    SendRequest("unhook_method", request);
  }

  private bool TryRemoveHookCallback(LocalHookCallback callback, out int token)
  {
    if (_hookCallbacksToTokens.TryRemove(callback, out token))
    {
      _tokensToHookCallbacks.TryRemove(token, out _);
      return true;
    }

    foreach (var entry in _tokensToHookCallbacks)
    {
      if (entry.Value != callback)
        continue;

      if (_tokensToHookCallbacks.TryRemove(entry.Key, out _))
      {
        token = entry.Key;
        _hookCallbacksToTokens.TryRemove(callback, out _);
        return true;
      }
    }

    return false;
  }

  public delegate void LocalEventCallback(ObjectOrRemoteAddress[] args);

  public void Dispose()
  {
    _tcp?.Dispose();
  }
}
