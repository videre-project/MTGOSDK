/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using MessagePack;

using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Memory.Snapshot;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

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
      {"/invoke", _ => MakeInvokeResponse()},
      {"/get_field", _ => MakeGetFieldResponse()},
      {"/set_field", _ => MakeSetFieldResponse()},
      {"/unpin", _ => MakeUnpinResponse()},
      {"/get_item", _ => MakeArrayItemResponse()},
      {"/event_subscribe", _ => MakeEventSubscribeResponse()},
      {"/event_unsubscribe", _ => MakeEventUnsubscribeResponse()},
      {"/hook_method", _ => MakeHookMethodResponse()},
      {"/unhook_method", _ => MakeUnhookMethodResponse()},
    };

    _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
    _remoteHooks = new ConcurrentDictionary<int, RegisteredMethodHookInfo>();
  }

  public void Start(ushort listenPort)
  {
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
    // Set the body in thread-local storage so endpoint handlers can use DeserializeRequest
    _cachedRequestBody.Value = body;

    if (_tcpHandlers.TryGetValue(endpoint, out var handler))
    {
      try
      {
        return handler(body);
      }
      catch (Exception ex)
      {
        return QuickError(ex.Message, ex.StackTrace);
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
