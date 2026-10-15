/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;
using MTGOSDK.Core.Remoting.Types;


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
  private volatile bool _registered;

  // This communicator's own registration within its process.
  private string _clientId = Guid.NewGuid().ToString("N");

  // Receipts and subscriptions keep the epoch in which they were acquired.
  // A new registration publishes an immutable snapshot. Reconnects reuse it,
  // and an older Diver keeps it after unregistering for explicit cleanup.
  // Registration requests and subscription cleanup share the lock. Pin returns
  // carry their owner in the request and do not hold this lock.
  private readonly object _registrationLock = new();
  private long _epochs;
  private sealed record Registration(long Epoch, string PinOwner);
  private volatile Registration _registration = new(0, "");
  private volatile UnregisterClientRequest? _pendingUnregister;

  internal long RegistrationEpoch => _registration.Epoch;
  internal string PinOwner => _registration.PinOwner;

  internal long GetResponseRegistrationEpoch(string? owner)
  {
    var registration = _registration;
    if (owner is not null && owner != registration.PinOwner)
      throw new ObjectDisposedException(nameof(DiverCommunicator),
        "The response belongs to an earlier client registration.");
    return registration.Epoch;
  }

  internal bool IsRegistrationCurrent(long epoch) =>
    !HasEnded(epoch) && !_isDisposed;

  // The epoch each event callback and hook callback was subscribed under (none:
  // subscribed while unregistered). The Diver removed those of an ended epoch
  // with the registration, so they need no remote cleanup.
  private readonly ConcurrentDictionary<LocalEventCallback, long> _eventEpochs = new();
  private readonly ConcurrentDictionary<LocalHookCallback, long> _hookEpochs = new();
  private Exception? _lastError;

  public Exception? LastError => _lastError;

  /// <summary>
  /// Reports ordered captures received by this connection. Captures can belong to
  /// subscriptions registered by other clients, so subscribers should filter by
  /// StreamId and use Sequence to process values in native completion order. Handlers run
  /// on the connection's callback delivery queue.
  /// </summary>
  public event Action<HookContext>? CaptureCompleted;

  private readonly record struct CallbackRegistration<TCallback>(
    TCallback Callback, IReadOnlyList<RemoteObject>? RetainedInputs) where TCallback : Delegate;

  private static IReadOnlyList<RemoteObject>? GetRetainedInputs(RemoteBatchRequest? batch) =>
    batch?.Inputs.Where(input => input.RetainedObject is not null)
      .Select(input => input.RetainedObject!).ToArray();

  // Callback handling
  private readonly ConcurrentDictionary<int, CallbackRegistration<LocalEventCallback>> _tokensToEventHandlers = new();
  private readonly ConcurrentDictionary<LocalEventCallback, int> _eventHandlersToToken = new();
  private readonly ConcurrentDictionary<int, CallbackRegistration<LocalHookCallback>> _tokensToHookCallbacks = new();
  private readonly ConcurrentDictionary<LocalHookCallback, int> _hookCallbacksToTokens = new();

  private static readonly AsyncLocal<bool> s_forceUIThread = new();
  private volatile bool _isDisposed = false;
  private int _disposeStarted;

  // Pin references returned in the background (see ReleaseLater), in order.
  private readonly ChannelScheduler _releaseScheduler = new(1);

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

  /// <summary>
  /// Unregisters this client. Does nothing if it is not connected or not
  /// registered (for example after <see cref="TryUnregisterClient"/>).
  /// </summary>
  public bool Disconnect()
  {
    if (!IsConnected || !_registered) return false;
    return UnregisterClient(_process_id);
  }

  public void Cancel() => Dispose();

  public DiverCommunicator(
    string hostname,
    int diverPort,
    CancellationTokenSource cts = null)
  {
    _tcp = new TcpCommunicator(hostname, diverPort, cts);

    // Register callback handler for incoming events/hooks from Diver
    _tcp.SetCallbackHandler(HandleCallback);

    RemoteClient.Disposed += OnRemoteClientDisposed;
  }

  private void OnRemoteClientDisposed(object? sender, EventArgs args)
  {
    _isDisposed = true;
    SyncThread.Enqueue(Dispose);
  }

  /// <summary>
  /// Connect to the Diver via TCP. A registered client's new connection is
  /// registered again before any other request is sent on it.
  /// </summary>
  public Task ConnectAsync(CancellationToken cancellationToken = default) =>
    ConnectRegisteredAsync(cancellationToken);

  /// <summary>
  /// Handles callbacks (events/hooks) received from the Diver.
  /// </summary>
  private void HandleCallback(string endpoint, byte[] body)
  {
    if (endpoint == "/invoke_callback")
    {
      var request = MessagePackSerializer.Deserialize<CallbackInvocationRequest>(body);
      if (request.PinOwner is not null && request.PinOwner != PinOwner) return;
      foreach (var argument in request.Parameters)
        argument.PinOwner = request.PinOwner;
      Interlocked.Increment(ref s_callbacksReceived);
      // Normalize both timestamps to UTC before measuring delivery latency because
      // the deserialized DateTime kind may differ from the Diver's original timestamp.
      double latencyMs = (DateTime.UtcNow - request.Timestamp.ToUniversalTime())
        .TotalMilliseconds;
      s_lastCallbackLatencyMs = latencyMs;
      s_avgCallbackLatencyMs = s_avgCallbackLatencyMs == 0
        ? latencyMs
        : s_avgCallbackLatencyMs * 0.9 + latencyMs * 0.1;

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

      var context = new HookContext(request.Timestamp)
      {
        Capture = request.Capture,
        CaptureError = request.CaptureError,
        CaptureSequence = request.CaptureSequence,
        OwnsParameters = request.ParametersPinnedPerRecipient
      };
      if (request.CaptureSequence is not null && CaptureCompleted is { } handlers)
        foreach (Action<HookContext> handler in handlers.GetInvocationList())
        {
          try { handler(context); }
          catch (Exception error) { Log.Error(error, "Native capture completion handler failed."); }
        }

      if (request.Parameters.Count > 0)
        request.Parameters[0].Timestamp = request.Timestamp;

      // A current Diver pins each object argument once for every client it sends
      // the callback to. A handler takes over this client's references (see
      // ReleaseCallbackArguments); a callback without one returns them here.
      if (_tokensToEventHandlers.TryGetValue(request.Token, out var subscription))
      {
        if (request.Parameters.Count > 1)
          request.Parameters[1].Timestamp = request.Timestamp;

        subscription.Callback(context, [.. request.Parameters]);
      }
      else if (_tokensToHookCallbacks.TryGetValue(request.Token, out var hook))
      {
        hook.Callback(context,
             request.Parameters.FirstOrDefault() ?? ObjectOrRemoteAddress.Null,
             [.. request.Parameters.Skip(1)]);
      }
      else
      {
        ReleaseCallbackArguments(context, request.Parameters);
      }
    }
  }

  /// <summary>
  /// Returns this client's pin reference to each object argument of a callback
  /// that will not be resolved to a RemoteObject (which would take it over).
  /// Nothing is returned here to a Diver that pinned the arguments once for
  /// all recipients (<see cref="HookContext.OwnsParameters"/> is false).
  /// </summary>
  internal void ReleaseCallbackArguments(HookContext context, IEnumerable<ObjectOrRemoteAddress> arguments)
  {
    if (!context.OwnsParameters) return;
    foreach (var argument in arguments)
    {
      if (argument is null || !argument.IsRemoteAddress || argument.IsNull || argument.IsType)
        continue;
      ReleaseLater(argument.RemoteAddress, argument.PinOwner);
    }
  }

  /// <summary>
  /// Returns one pin reference in the background, so the caller (a read or the
  /// in-order callback queue) does not wait for the round trip. The caller must
  /// own the reference; holding it a little longer is harmless.
  /// </summary>
  /// <remarks>
  /// After <see cref="Dispose"/> a release is dropped: the Diver releases this
  /// client's references when it unregisters or its process exits.
  /// </remarks>
  internal void ReleaseLater(ulong address) =>
    ReleaseLater(address, RegistrationEpoch);

  internal void ReleaseLater(ulong address, string? owner)
  {
    var registration = _registration;
    if (owner is not null && owner != registration.PinOwner) return;
    ReleaseLater(address, registration.Epoch);
  }

  internal void ReleaseLater(ulong address, long epoch) =>
    _releaseScheduler.Enqueue(() =>
    {
      if (_isDisposed) return;
      try
      {
        UnpinObject(address, epoch);
      }
      catch (Exception ex)
      {
        Log.Debug("Failed to return pin reference {Address}: {Message}", address, ex.Message);
      }
    });

  private static ReadOnlyMemory<byte> Serialize<T>(T value) =>
    MessagePackSerializer.Serialize(value);

  /// <summary>
  /// Ensures the TCP connection is established.
  /// </summary>
  private void EnsureConnected(CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(DiverCommunicator), "MTGO process has closed");

    if (!_tcp.IsConnected)
      ConnectRegisteredAsync(cancellationToken).GetAwaiter().GetResult();

    if (_pendingUnregister is not null)
    {
      bool locked = false;
      try
      {
        while (!locked)
        {
          cancellationToken.ThrowIfCancellationRequested();
          Monitor.TryEnter(_registrationLock, s_cleanupLockSlice, ref locked);
        }
        if (_pendingUnregister is not null)
        {
          _tcp.SendRequestAsync<UnregisterClientResponse>("/unregister_client",
            MessagePackSerializer.Serialize(_pendingUnregister), cancellationToken).GetAwaiter().GetResult();
          _pendingUnregister = null;
        }
      }
      finally
      {
        if (locked) Monitor.Exit(_registrationLock);
      }
    }
  }

  /// <summary>
  /// Connects (unless connected). A registered client's new connection is
  /// registered again as its first request, before any other request can be
  /// sent on it, so it receives callbacks and the pins taken and returned
  /// over it stay counted as this client's. If that registration fails, the
  /// connection is closed and the error is thrown; the next request retries.
  /// </summary>
  private async Task ConnectRegisteredAsync(CancellationToken cancellationToken)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(DiverCommunicator));
    Func<Func<string, byte[], Task>, Task>? handshake = null;
    if (_registered && _process_id is int pid)
    {
      byte[] registration = MessagePackSerializer.Serialize(
        new RegisterClientRequest { ProcessId = pid, ClientId = _clientId });
      handshake = send => send("/register_client", registration);
    }

    try
    {
      await _tcp.ConnectAsync(handshake, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (handshake != null && ex is not OperationCanceledException)
    {
      Log.Warning($"Failed to register the reconnected Diver connection: {ex.Message}");
      throw new InvalidOperationException(
        "The reconnected Diver connection could not be registered; the request was not sent.", ex);
    }
  }

  /// <summary>
  /// Sends a request and returns the response.
  /// </summary>
  private T SendRequest<T>(string endpoint, object request = null,
    CancellationToken cancellationToken = default)
  {
    EnsureConnected(cancellationToken);
    Interlocked.Increment(ref s_totalRequests);

    byte[] body = request != null ? MessagePackSerializer.Serialize(request) : null;
    var sw = Stopwatch.StartNew();
    try
    {
      return _tcp.SendRequestAsync<T>("/" + endpoint, body, cancellationToken).GetAwaiter().GetResult();
    }
    finally
    {
      sw.Stop();
      var m = s_ipcMetrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
      Interlocked.Increment(ref m.Count);
      Interlocked.Add(ref m.TotalTicks, sw.ElapsedTicks);
      Interlocked.Exchange(ref m.LastTicks, sw.ElapsedTicks);
      GC.KeepAlive(request);
    }
  }

  private void SendRequest(string endpoint, object request = null,
    CancellationToken cancellationToken = default) =>
    SendRequest<object>(endpoint, request, cancellationToken);

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

  public void UnpinObject(ulong address) =>
    UnpinObject(address, RegistrationEpoch);

  internal void UnpinObject(ulong address, long epoch)
  {
    var registration = _registration;
    if (_isDisposed || registration.Epoch != epoch) return;
    using var cts = new CancellationTokenSource(s_pinReleaseTimeout);
    SendRequest("unpin", new UnpinRequest { Address = address, PinOwner = registration.PinOwner }, cts.Token);
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
    lock (_registrationLock)
    {
      _process_id = process_id ?? Process.GetCurrentProcess().Id;
      _lastError = null;

      try
      {
        var request = new RegisterClientRequest { ProcessId = _process_id.Value, ClientId = _clientId };
        SendRequest("register_client", request);
        string owner = _clientId is not null ? $"{_process_id}/{_clientId}" : "";
        if (_clientId is not null && _registration.PinOwner != owner)
          _registration = new Registration(++_epochs, owner);
        _registered = true;
        return true;
      }
      catch (Exception ex)
      {
        _lastError = ex;
        return false;
      }
    }
  }

  public bool UnregisterClient(int? process_id = null)
  {
    lock (_registrationLock)
    {
      var request = new UnregisterClientRequest
      {
        ProcessId = process_id ?? _process_id ?? Process.GetCurrentProcess().Id,
        ClientId = _clientId
      };
      try
      {
        SendRequest<UnregisterClientResponse>("unregister_client", request);
        return true;
      }
      catch
      {
        if (request.ClientId is not null && _tcp.SupportsPinOwnership) _pendingUnregister = request;
        return false;
      }
      finally
      {
        EndRegistration(request);
      }
    }
  }

  /// <summary>
  /// Invalidates local holders once unregistering was attempted. A lost response
  /// leaves cleanup pending, so the next request confirms that the old client is
  /// absent before proceeding. A fresh client id prevents a delayed unregister
  /// from removing a later registration. An older Diver retains shared references
  /// and subscriptions, so local holders remain available for explicit cleanup.
  /// </summary>
  private void EndRegistration(UnregisterClientRequest request)
  {
    bool reclaimingRegistration = _registered && request.ClientId is not null &&
      _tcp.SupportsPinOwnership;
    _registered = false;
    _process_id = null;
    if (reclaimingRegistration)
    {
      _registration = new Registration(++_epochs, "");
      _clientId = Guid.NewGuid().ToString("N");
    }
  }

  /// <summary>
  /// Whether a subscription made under <paramref name="epoch"/> was removed by
  /// the Diver with its registration. Never waits for a request in progress.
  /// </summary>
  private bool HasEnded(long epoch) => _registration.Epoch != epoch;

  /// <summary>
  /// Unregisters this client if it is registered and connected, waiting at
  /// most <paramref name="timeout"/> for the Diver: when the client is being
  /// disposed, so the Diver releases its pins and subscriptions at once
  /// instead of when its process exits. Never reconnects; false if the
  /// Diver did not confirm in time.
  /// </summary>
  public bool TryUnregisterClient(TimeSpan timeout)
  {
    if (!_registered || _process_id is null || !_tcp.IsConnected)
      return false;
    var watch = Stopwatch.StartNew();
    // A subscription or registration still waiting for the Diver holds the
    // lock; disposal does not wait for it beyond the timeout.
    bool locked = false;
    try
    {
      Monitor.TryEnter(_registrationLock, timeout, ref locked);
      if (!locked)
      {
        Log.Debug("Unregistering from the Diver did not start in time.");
        return false;
      }
      if (!_registered || _process_id is not int currentPid) return false;
      TimeSpan left = timeout - watch.Elapsed;
      using var cts = new CancellationTokenSource(left > TimeSpan.Zero ? left : TimeSpan.Zero);
      var request = new UnregisterClientRequest { ProcessId = currentPid, ClientId = _clientId };
      try
      {
        _tcp.SendRequestAsync<UnregisterClientResponse>(
          "/unregister_client", MessagePackSerializer.Serialize(request), cts.Token)
          .GetAwaiter().GetResult();
        return true;
      }
      catch
      {
        if (request.ClientId is not null && _tcp.SupportsPinOwnership) _pendingUnregister = request;
        throw;
      }
      finally
      {
        EndRegistration(request);
      }
    }
    catch (Exception ex)
    {
      Log.Debug("Unregistering from the Diver did not complete: {Message}", ex.Message);
      return false;
    }
    finally
    {
      if (locked) Monitor.Exit(_registrationLock);
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
  /// Fetches properties for all items in a collection in a single IPC call,
  /// pinning each item and returning its token (the caller owns one pin
  /// reference per returned token and must return the ones it does not use).
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
    int maxItems = 0) =>
    GetBatchCollectionMembers(collectionAddr, collectionTypeName, pathsDelimited, maxItems, pinItems: true);

  /// <summary>
  /// Fetches properties for all items in a collection in a single IPC call.
  /// </summary>
  /// <param name="collectionAddr">Address of the IEnumerable collection.</param>
  /// <param name="collectionTypeName">Type name of the collection.</param>
  /// <param name="pathsDelimited">Pipe-delimited property paths.</param>
  /// <param name="maxItems">Maximum items to process (0 = no limit).</param>
  /// <param name="pinItems">
  /// Whether to pin each item and return its token; the caller then owns one
  /// pin reference per returned token and must return the ones it does not use.
  /// </param>
  /// <returns>Response containing all items' property values.</returns>
  public Interactions.Object.BatchCollectionResponse GetBatchCollectionMembers(
    ulong collectionAddr,
    string collectionTypeName,
    string pathsDelimited,
    int maxItems,
    bool pinItems)
  {
    var request = new Interactions.Object.BatchCollectionRequest
    {
      CollectionAddress = collectionAddr,
      CollectionTypeName = collectionTypeName,
      PathsDelimited = pathsDelimited,
      MaxItems = maxItems,
      SkipItemPins = !pinItems
    };
    return SendRequest<Interactions.Object.BatchCollectionResponse>("batch_collection", request);
  }

  /// <summary>
  /// Async variant of <see cref="GetBatchCollectionMembers(ulong, string, string, int)"/>
  /// that does not block a thread pool thread while waiting for the IPC response.
  /// </summary>
  public Task<Interactions.Object.BatchCollectionResponse> GetBatchCollectionMembersAsync(
    ulong collectionAddr,
    string collectionTypeName,
    string pathsDelimited,
    int maxItems = 0) =>
    GetBatchCollectionMembersAsync(collectionAddr, collectionTypeName, pathsDelimited, maxItems, pinItems: true);

  /// <summary>
  /// Async variant of <see cref="GetBatchCollectionMembers(ulong, string, string, int, bool)"/>
  /// that does not block a thread pool thread while waiting for the IPC response.
  /// </summary>
  public Task<Interactions.Object.BatchCollectionResponse> GetBatchCollectionMembersAsync(
    ulong collectionAddr,
    string collectionTypeName,
    string pathsDelimited,
    int maxItems,
    bool pinItems)
  {
    EnsureConnected();
    var request = new Interactions.Object.BatchCollectionRequest
    {
      CollectionAddress = collectionAddr,
      CollectionTypeName = collectionTypeName,
      PathsDelimited = pathsDelimited,
      MaxItems = maxItems,
      SkipItemPins = !pinItems
    };
    byte[] body = MessagePackSerializer.Serialize(request);
    return _tcp.SendRequestAsync<Interactions.Object.BatchCollectionResponse>(
      "/batch_collection", body);
  }

  /// <summary>
  /// Executes a composed reflection batch in one IPC request.
  /// </summary>
  public RemoteBatchResult ExecuteBatch(RemoteBatchRequest request) =>
    ExecuteBatchAsync(request).GetAwaiter().GetResult();

  /// <summary>
  /// Sends one batch request and asynchronously awaits the result. The Diver still
  /// executes native calls synchronously and applies any ambient UI dispatch setting
  /// to the whole batch.
  /// </summary>
  public async Task<RemoteBatchResult> ExecuteBatchAsync(RemoteBatchRequest request) =>
    await ExecuteDetachedBatchAsync(request.Copy()).ConfigureAwait(false);

  /// <summary>
  /// Takes ownership of an already detached request from the batch builder.
  /// </summary>
  internal async Task<RemoteBatchResult> ExecuteDetachedBatchAsync(RemoteBatchRequest request)
  {
    request.ForceUIThread |= ForceUIThread;
    EnsureConnected();
    Interlocked.Increment(ref s_totalRequests);
    var sw = Stopwatch.StartNew();
    try
    {
      return await _tcp.SendRequestAsync<RemoteBatchResult>("/batch_execute",
        MessagePackSerializer.Serialize(request)).ConfigureAwait(false);
    }
    finally
    {
      sw.Stop();
      var metrics = s_ipcMetrics.GetOrAdd("batch_execute", _ => new EndpointMetrics());
      Interlocked.Increment(ref metrics.Count);
      Interlocked.Add(ref metrics.TotalTicks, sw.ElapsedTicks);
      Interlocked.Exchange(ref metrics.LastTicks, sw.ElapsedTicks);
      GC.KeepAlive(request);
    }
  }

  public RemoteCaptureSequence EventSubscribe(
    ulong targetAddr,
    string eventName,
    LocalEventCallback callback,
    RemoteEventCapture capture = null, string[] propertyNames = null)
  {
    var inputs = GetRetainedInputs(capture?.Batch);
    var request = new EventSubscriptionRequest
    {
      Address = targetAddr,
      EventName = eventName,
      Capture = capture,
      PropertyNames = propertyNames
    };
    // The epoch it is made under, taken before the request: no registration
    // change overlaps it.
    lock (_registrationLock)
    {
      long epoch = _registered ? RegistrationEpoch : 0;
      var regRes = SendRequest<EventRegistrationResults>("event_subscribe", request);
      if (epoch != 0) _eventEpochs[callback] = epoch;
      else _eventEpochs.TryRemove(callback, out _);
      _tokensToEventHandlers[regRes.Token] = new(callback, inputs);
      _eventHandlersToToken[callback] = regRes.Token;
      return regRes.CaptureSequence;
    }
  }

  internal void SetEventPropertyFilter(LocalEventCallback callback, string[] propertyNames)
  {
    if (!_eventHandlersToToken.TryGetValue(callback, out int token))
      throw new InvalidOperationException("Event callback is not registered.");
    SendRequest("event_property_filter", new EventPropertyFilterRequest
    { Token = token, PropertyNames = propertyNames });
  }

  public void EventUnsubscribe(LocalEventCallback callback)
  {
    if (!TryRemoveCallback(callback, _tokensToEventHandlers, _eventHandlersToToken,
      out int token, out var inputs))
    {
      if (_isDisposed) return;
      throw new Exception("EventUnsubscribe: callback not found");
    }

    _eventEpochs.TryRemove(callback, out long epoch);
    try
    {
      SendSubscriptionCleanup(epoch, "event_unsubscribe", new EventUnsubscriptionRequest { Token = token },
        unregisteredSubscription: epoch == 0);
    }
    finally
    {
      GC.KeepAlive(inputs);
    }
  }

  /// <summary>
  /// Sends subscription cleanup only for the registration that acquired it.
  /// The registration lock covers the request so registering again
  /// cannot redirect it to another owner. A retired cleanup returns without
  /// waiting for a registration request already in progress. A subscription
  /// created before registration can still send cleanup while unregistered.
  /// </summary>
  private void SendSubscriptionCleanup(long epoch, string endpoint, object request,
    bool unregisteredSubscription = false)
  {
    bool IsRetired() => HasEnded(epoch) &&
      !(unregisteredSubscription && PinOwner.Length == 0);

    bool locked = false;
    try
    {
      while (!locked)
      {
        if (IsRetired())
          return;
        if (_isDisposed)
          return;
        Monitor.TryEnter(_registrationLock, s_cleanupLockSlice, ref locked);
      }
      if (IsRetired())
        return;

      SendRequest(endpoint, request);
    }
    catch (ObjectDisposedException ex) when (IsRemoteShutdown(ex))
    {
      // The local callback maps have already been cleaned up. Once MTGO/Diver
      // is gone, there is no remote subscription or hook left to remove.
    }
    finally
    {
      if (locked) Monitor.Exit(_registrationLock);
    }
  }

  /// <summary>
  /// Bounds pin returns so an unanswered request cannot stall the release queue.
  /// </summary>
  private static readonly TimeSpan s_pinReleaseTimeout = TimeSpan.FromSeconds(1);

  /// <summary>
  /// How long cleanup waits for the registration lock before checking its epoch.
  /// </summary>
  private static readonly TimeSpan s_cleanupLockSlice = TimeSpan.FromMilliseconds(50);

  public bool HookMethod(
    string type,
    string methodName,
    HarmonyPatchPosition pos,
    LocalHookCallback callback,
    List<string> parametersTypeFullNames = null,
    RemoteHookCapture capture = null)
  {
    var inputs = GetRetainedInputs(capture?.Batch);
    var req = new HookSubscriptionRequest
    {
      TypeFullName = type,
      MethodName = methodName,
      HookPosition = pos.ToString(),
      ParametersTypeFullNames = parametersTypeFullNames,
      Capture = capture
    };

    // The epoch it is made under, taken before the request: no registration
    // change overlaps it.
    lock (_registrationLock)
    {
      long epoch = _registered ? RegistrationEpoch : 0;
      var regRes = SendRequest<EventRegistrationResults>("hook_method", req);
      if (epoch != 0) _hookEpochs[callback] = epoch;
      else _hookEpochs.TryRemove(callback, out _);
      _tokensToHookCallbacks[regRes.Token] = new(callback, inputs);
      _hookCallbacksToTokens[callback] = regRes.Token;
      return true;
    }
  }

  public void UnhookMethod(LocalHookCallback callback)
  {
    if (!TryRemoveCallback(callback, _tokensToHookCallbacks, _hookCallbacksToTokens,
      out int token, out var inputs))
    {
      if (_isDisposed) return;
      throw new Exception("UnhookMethod: callback not found");
    }

    _hookEpochs.TryRemove(callback, out long epoch);
    try
    {
      SendSubscriptionCleanup(epoch, "unhook_method", new HookUnsubscriptionRequest { Token = token },
        unregisteredSubscription: epoch == 0);
    }
    finally
    {
      GC.KeepAlive(inputs);
    }
  }

  private bool IsRemoteShutdown(ObjectDisposedException ex) =>
    _isDisposed ||
    ex.ObjectName == nameof(DiverCommunicator) ||
    ex.Message.Contains("MTGO process has closed", StringComparison.OrdinalIgnoreCase);

  private static bool TryRemoveCallback<TCallback>(TCallback callback,
    ConcurrentDictionary<int, CallbackRegistration<TCallback>> registrations,
    ConcurrentDictionary<TCallback, int> tokens, out int token,
    out IReadOnlyList<RemoteObject>? inputs) where TCallback : Delegate
  {
    inputs = null;
    if (tokens.TryRemove(callback, out token))
    {
      // Remove only this callback's entry: a later subscription can reuse the token.
      if (registrations.TryGetValue(token, out var registration) &&
          registration.Callback.Equals(callback))
      {
        inputs = registration.RetainedInputs;
        ((ICollection<KeyValuePair<int, CallbackRegistration<TCallback>>>)registrations)
          .Remove(new(token, registration));
      }
      return true;
    }

    foreach (var entry in registrations)
    {
      if (!entry.Value.Callback.Equals(callback))
        continue;
      if (((ICollection<KeyValuePair<int, CallbackRegistration<TCallback>>>)registrations)
          .Remove(entry))
      {
        token = entry.Key;
        inputs = entry.Value.RetainedInputs;
        tokens.TryRemove(callback, out _);
        return true;
      }
    }

    return false;
  }

  public delegate void LocalEventCallback(HookContext context, ObjectOrRemoteAddress[] args);

  /// <summary>
  /// Closes the connection. Queued pin releases are drained first, which can
  /// wait for the Diver for a few seconds if it does not answer.
  /// </summary>
  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
    RemoteClient.Disposed -= OnRemoteClientDisposed;
    try
    {
      _releaseScheduler.Dispose();
    }
    finally
    {
      _isDisposed = true;
      _tcp.Dispose();
      // Closing TCP completes any pending registration or subscription request
      // before its maps are cleared under the same lock used to publish them.
      lock (_registrationLock)
      {
        _registered = false;
        _process_id = null;
        _pendingUnregister = null;
        _tokensToEventHandlers.Clear();
        _eventHandlersToToken.Clear();
        _tokensToHookCallbacks.Clear();
        _hookCallbacksToTokens.Clear();
        _eventEpochs.Clear();
        _hookEpochs.Clear();
        CaptureCompleted = null;
      }
    }
  }
}
