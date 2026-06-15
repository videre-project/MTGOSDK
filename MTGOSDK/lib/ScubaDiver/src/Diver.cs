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

using MTGOSDK.Core.Diagnostics;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Memory.Snapshot;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Resources;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private SnapshotRuntime _runtime;
  private TcpServer _tcpServer;

  private static readonly AsyncLocal<byte[]> _cachedRequestBody = new();

  private readonly Dictionary<string, Func<byte[], byte[]>> _tcpHandlers;
  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
  private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;

  private readonly CancellationTokenSource _cts = new();

  private readonly ConcurrentDictionary<int, HashSet<int>> _clientCallbacks = new();
  private readonly ConcurrentDictionary<int, CancellationTokenSource> _callbackTokens = new();

  public Diver()
  {
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
      {"/event_subscribe", _ => MakeEventSubscribeResponse()},
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
#if MTGOSDK_TRACING
    _traceExporter = new TraceExporter(
        Path.Combine(Bootstrapper.AppDataDir, "Logs", "trace", "trace_diver.json"),
        "ScubaDiver");
#endif

    _runtime = new SnapshotRuntime();

    _tcpServer = new TcpServer(listenPort, HandleTcpRequest);
    Log.Debug($"[Diver] Listening on TCP port {listenPort}...");
    
    _tcpServer.StartAsync(_cts.Token).GetAwaiter().GetResult();

    Log.Debug("[Diver] Closing ClrMD runtime and snapshot");
    this.Dispose();

    Log.Debug("[Diver] Exiting");
  }

  /// <summary>
  /// Routes TCP requests to the appropriate handler.
  /// </summary>
  private byte[] HandleTcpRequest(string endpoint, byte[] body)
  {
#if MTGOSDK_TRACING
    // Unwrap the TracedRequest
    TracedRequest tracedReq;
    try 
    {
      tracedReq = TracedRequest.Deserialize(body);
    }
    catch (Exception ex)
    {
      // Fallback for legacy/non-traced requests (e.g. ping before tracing enabled)
      // or if deser fails, we assume it's a raw body
      Log.Error($"[Diver] Failed to deserialize TracedRequest: {ex.GetType().Name} - {ex.Message}");
      tracedReq = new TracedRequest { Body = body };
    }

    ActivityContext parentContext = default;
    if (!string.IsNullOrEmpty(tracedReq.TraceParent))
    {
        ActivityContext.TryParse(tracedReq.TraceParent, tracedReq.TraceState, out parentContext);
    }

    // Start metadata-rich activity for the request
    using var activity = s_activitySource.StartActivity(
        endpoint, 
        ActivityKind.Server, 
        parentContext);
    
    if (activity != null)
    {
        activity.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());
        activity.SetTag("endpoint", endpoint);
        activity.SetTag("ipc.flow", "end"); // Tag for flow event visualization
    }

    // Set the actual request body in thread-local storage
    _cachedRequestBody.Value = tracedReq.Body;
#else
    // Tracing is disabled unless MTGOSDK_TRACING is defined
    Activity activity = null;
    _cachedRequestBody.Value = body;
#endif

    if (_tcpHandlers.TryGetValue(endpoint, out var handler))
    {
      var sw = Stopwatch.StartNew();
      try
      {
        var result = handler(_cachedRequestBody.Value);
        return result;
      }
      catch (Exception ex)
      {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        return QuickError(ex.Message, ex.StackTrace);
      }
      finally
      {
        sw.Stop();
        var m = s_endpointMetrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
        Interlocked.Increment(ref m.Count);
        Interlocked.Add(ref m.TotalTicks, sw.ElapsedTicks);
        Interlocked.Exchange(ref m.LastTicks, sw.ElapsedTicks);
      }
    }
    return QuickError($"Unknown endpoint: {endpoint}");
  }

  /// <summary>
  /// Sends a callback to the connected SDK client over TCP.
  /// </summary>
  public void SendTcpCallback(CallbackInvocationRequest callback)
  {
    _tcpServer?.SendCallback(callback);
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

  public static byte[] WrapSuccess<T>(T data) =>
    MessagePackSerializer.Serialize(DiverResponse<T>.Success(data));

  /// <summary>
  /// Get the cached request body for the current request.
  /// </summary>
  public static byte[] ReadRequestBody() =>
    _cachedRequestBody.Value ?? Array.Empty<byte>();

  /// <summary>
  /// Deserialize the cached request body.
  /// </summary>
  public static T DeserializeRequest<T>() =>
    MessagePackSerializer.Deserialize<T>(_cachedRequestBody.Value);

  public void Dispose()
  {
    _cts.Cancel();
    _cts.Dispose();
    _tcpServer?.Dispose();
    _runtime?.Dispose();
#if MTGOSDK_TRACING
    _traceExporter?.Dispose();
#endif
    _clientCallbacks.Clear();
    STAThread.Stop();

    // Clean up event subscriptions and hooks
    foreach (RegisteredEventHandlerInfo rehi in _remoteEventHandler.Values)
    {
      rehi.EventInfo.RemoveEventHandler(rehi.Target, rehi.RegisteredProxy);
    }
    foreach (RegisteredMethodHookInfo rmhi in _remoteHooks.Values)
    {
      HarmonyWrapper.Instance.RemovePrefix(rmhi.OriginalHookedMethod);
    }
    _remoteEventHandler.Clear();
    _remoteHooks.Clear();
  }
}
