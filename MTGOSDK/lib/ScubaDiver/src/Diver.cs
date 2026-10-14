/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

using MessagePack;

using MTGOSDK.Core;
using MTGOSDK.Core.Diagnostics;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Memory.Snapshot;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Resources;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  // Use one sender to preserve callback enqueue order over TCP.
  private readonly ChannelScheduler _callbackScheduler = new(1);
  private SnapshotRuntime _runtime;
  private TcpServer _tcpServer;
  private readonly BatchExecutionEndpoint _batchExecutionEndpoint;
  private readonly BatchExecutor _batchExecutor;
  private readonly CaptureScopeManager _captureScopes = new();

  private static readonly AsyncLocal<byte[]> s_requestBody = new();
  private static readonly AsyncLocal<Guid?> s_requestConnection = new();
  private static readonly AsyncLocal<RequestOwner?> s_requestOwner = new();
  private static readonly AsyncLocal<string> s_requestPinOwner = new();

  private readonly Dictionary<string, Func<byte[], byte[]>> _tcpHandlers;
  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
  private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;
  private readonly object _methodHookGate = new();

  private readonly CancellationTokenSource _cts = new();
  private int _disposed;

  private readonly ConcurrentDictionary<int, byte> _activeCallbacks = new();

  public Diver()
  {
    _batchExecutor = new BatchExecutor(
      type => BatchExecutionEndpoint.ResolveLoadedType(type, name => _runtime.ResolveType(name)),
      input => _runtime.ParseParameterObject(input), instance => _runtime.GetObjectToken(instance));
    _batchExecutionEndpoint = new BatchExecutionEndpoint(_batchExecutor,
      action => STAThread.IsDispatcherThread ? action() : STAThread.Execute(action));

    // TCP handlers for request dispatch
    _tcpHandlers = new Dictionary<string, Func<byte[], byte[]>>()
    {
      {"/ping", _ => WrapSuccess("pong")},
      {"/register_client", _ => MakeRegisterClientResponse()},
      {"/unregister_client", _ => MakeUnregisterClientResponse()},
      {"/domains", _ => MakeDomainsResponse()},
      {"/heap", _ => MakeHeapResponse()},
      {"/types", _ => MakeTypesResponse()},
      {"/type", _ => MakeTypeResponse()},
      {"/object", _ => MakeObjectResponse()},
      {"/create_object", _ => MakeCreateObjectResponse()},
      {"/create_array", _ => MakeCreateArrayResponse()},
      {"/invoke", _ => MakeInvokeResponse()},
      {"/get_field", _ => MakeGetFieldResponse()},
      {"/set_field", _ => MakeSetFieldResponse()},
      {"/unpin", _ => MakeUnpinResponse()},
      {"/get_item", _ => MakeArrayItemResponse()},
      {"/batch_members", _ => MakeBatchMembersResponse()},
      {"/batch_collection", _ => MakeBatchCollectionResponse()},
      {"/batch_execute", body => _batchExecutionEndpoint.Handle(body)},
      {"/event_subscribe", _ => MakeEventSubscribeResponse()},
      {"/event_property_filter", _ => MakeEventPropertyFilterResponse()},
      {"/event_unsubscribe", _ => MakeEventUnsubscribeResponse()},
      {"/hook_method", _ => MakeHookMethodResponse()},
      {"/unhook_method", _ => MakeUnhookMethodResponse()},
      {"/diagnostics", _ => MakeDiagnosticsResponse()},
      {"/heap_snapshot", _ => MakeHeapSnapshotResponse()},
      {"/retain_chain", _ => MakeRetainChainResponse()},
      {"/type_instances", _ => MakeTypeInstancesResponse()},
      {"/static_holders", _ => MakeStaticHoldersResponse()},
    };

    _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
    _remoteHooks = new ConcurrentDictionary<int, RegisteredMethodHookInfo>();
  }

  private static readonly ActivitySource s_activitySource = new("ScubaDiver");
#if MTGOSDK_TRACING
  private TraceExporter _traceExporter;
#endif

  //
  // Diagnostics counters
  //

  internal sealed class EndpointMetrics
  {
    public long Count;
    public long TotalTicks;  // Stopwatch ticks for avg calculation
    public long LastTicks;
  }

  internal static readonly ConcurrentDictionary<string, EndpointMetrics>
    s_endpointMetrics = new();

  public void Start(ushort listenPort)
  {
    try
    {
#if MTGOSDK_TRACING
      _traceExporter = new TraceExporter(
        Path.Combine(Bootstrapper.AppDataDir, "Logs", "trace", "trace_diver.json"),
        "ScubaDiver");
#endif
      _runtime = new SnapshotRuntime();
      _tcpServer = new TcpServer(listenPort, HandleTcpRequest, OnConnectionClosed);
      StartClientMonitor();
      Log.Debug($"[Diver] Listening on TCP port {listenPort}...");
      _tcpServer.StartAsync(_cts.Token).GetAwaiter().GetResult();
    }
    finally
    {
      Log.Debug("[Diver] Closing ClrMD runtime and snapshot");
      Dispose();
    }

    Log.Debug("[Diver] Exiting");
  }

  /// <summary>
  /// Routes TCP requests to the appropriate handler.
  /// </summary>
  private byte[] HandleTcpRequest(Guid connection, string endpoint, byte[] body)
  {
#if MTGOSDK_TRACING
    using var activity = s_activitySource.StartActivity(endpoint, ActivityKind.Server);
    activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
    activity?.SetTag("endpoint", endpoint);
    activity?.SetTag("ipc.flow", "end");
#else
    Activity activity = null;
#endif
    if (!_tcpHandlers.TryGetValue(endpoint, out var handler))
      return QuickError($"Unknown endpoint: {endpoint}");

    var previousBody = s_requestBody.Value;
    var previousConnection = s_requestConnection.Value;
    s_requestBody.Value = body;
    s_requestConnection.Value = connection;
    NoteRequest(connection, endpoint);
    var previousOwner = s_requestOwner.Value;
    var previousPinOwner = s_requestPinOwner.Value;
    lock (_registeredPidsLock)
    {
      s_requestOwner.Value = ResolveRequestOwner();
      s_requestPinOwner.Value = _connectionClients.TryGetValue(connection, out var client)
        ? client.Key : "";
    }
    long started = Stopwatch.GetTimestamp();
    try
    {
      return handler(body);
    }
    catch (Exception ex)
    {
      activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
      return QuickError(ex.Message, ex.StackTrace);
    }
    finally
    {
      // Restore the enclosing request if a handler invoked another request locally.
      s_requestBody.Value = previousBody;
      s_requestConnection.Value = previousConnection;
      s_requestOwner.Value = previousOwner;
      s_requestPinOwner.Value = previousPinOwner;
      long elapsed = Stopwatch.GetTimestamp() - started;
      var metrics = s_endpointMetrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
      Interlocked.Increment(ref metrics.Count);
      Interlocked.Add(ref metrics.TotalTicks, elapsed);
      Interlocked.Exchange(ref metrics.LastTicks, elapsed);
    }
  }

  /// <summary>
  /// Sends a callback over TCP to the connected SDK clients that
  /// <paramref name="isRecipient"/> selects (see <see cref="TcpServer.SendCallback"/>).
  /// </summary>
  public int SendTcpCallback(
    Func<IReadOnlyList<Guid>, Func<Guid, CallbackInvocationRequest>> prepare,
    Action<IReadOnlyList<Guid>> releaseUnsent,
    Func<Guid, bool> isRecipient)
  {
    return _tcpServer?.SendCallback(prepare, releaseUnsent, isRecipient) ?? 0;
  }

  public byte[] QuickError(string error, string stackTrace = null)
  {
    stackTrace ??= new StackTrace(true).ToString();
    var errResponse = new DiverResponse<object>
    {
      IsError = true,
      ErrorMessage = error,
      ErrorStackTrace = stackTrace
    };
    return MessagePackSerializer.Serialize(errResponse);
  }

  public static byte[] WrapSuccess<T>(T data)
  {
    var response = DiverResponse<T>.Success(data);
    response.PinOwner = s_requestPinOwner.Value;
    return MessagePackSerializer.Serialize(response);
  }

  /// <summary>
  /// Get the cached request body for the current request.
  /// </summary>
  public static byte[] ReadRequestBody() =>
    s_requestBody.Value ?? Array.Empty<byte>();

  /// <summary>
  /// Deserialize the cached request body.
  /// </summary>
  public static T DeserializeRequest<T>() =>
    MessagePackSerializer.Deserialize<T>(s_requestBody.Value);



  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

    var failures = new List<Exception>();

    void Release(string resource, Action release)
    {
      try
      {
        release();
      }
      catch (Exception error)
      {
        failures.Add(error);
        Log.Error(error, "[Diver] Failed to release {0} during disposal.", resource);
      }
    }

    // Detach native callbacks before releasing the runtime they use for capture.
    Release("client monitor", () => _clientMonitor?.Dispose());
    Release("transport cancellation", _cts.Cancel);
    Release("TCP server", () => _tcpServer?.Dispose());
    _activeCallbacks.Clear();
    foreach (var registration in _remoteEventHandler.Values)
    {
      Release("event handler", () => RemoveEventHandler(
        registration.EventInfo, registration.Target, registration.RegisteredProxy));
      Release("event capture scope", () => registration.CaptureScopeSubscription?.Dispose());
    }
    _remoteEventHandler.Clear();
    lock (_methodHookGate)
    {
      foreach (var registration in _remoteHooks.Values)
        Release("method hook", () => HarmonyWrapper.Instance.RemoveHook(registration.OriginalHookedMethod));
      _remoteHooks.Clear();
    }

    Release("callback scheduler", _callbackScheduler.Dispose);
    Release("capture scopes", _captureScopes.Dispose);
    Release("snapshot runtime", () => _runtime?.Dispose());
    Release("STA thread", STAThread.Stop);
    lock (_registeredPidsLock)
    {
      _registeredClients.Clear();
      _legacyClients.Clear();
      _requestingConnections.Clear();
      _unregisteredSubscriptions.Clear();
      _connectionClients.Clear();
    }
#if MTGOSDK_TRACING
    Release("trace exporter", () => _traceExporter?.Dispose());
#endif
    Release("transport cancellation source", _cts.Dispose);

    if (failures.Count != 0)
      throw new AggregateException("Diver disposal failed to release one or more resources.", failures);
  }
}
