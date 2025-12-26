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
using System.Net;
using System.Threading;
using System.Threading.Tasks;

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
  private const string MsgPackContentType = "application/msgpack";

  private SnapshotRuntime _runtime;

  private readonly Dictionary<string, Func<HttpListenerRequest, byte[]>> _responseBodyCreators;

  private static readonly AsyncLocal<byte[]> _cachedRequestBody = new();

  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
  private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;

  private readonly CancellationTokenSource _cts = new();

  private readonly ConcurrentDictionary<int, HashSet<int>> _clientCallbacks = new();
  private readonly ConcurrentDictionary<int, CancellationTokenSource> _callbackTokens = new();

  public Diver()
  {
    _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, byte[]>>()
    {
      // Diver maintenance
      {"/ping", MakePingResponse},
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
      {"/get_item", MakeArrayItemResponse},
      // Callbacks
      {"/event_subscribe", MakeEventSubscribeResponse},
      {"/event_unsubscribe", MakeEventUnsubscribeResponse},
      // Harmony
      {"/hook_method", MakeHookMethodResponse},
      {"/unhook_method", MakeUnhookMethodResponse},
    };
    _remoteEventHandler = new ConcurrentDictionary<int, RegisteredEventHandlerInfo>();
    _remoteHooks = new ConcurrentDictionary<int, RegisteredMethodHookInfo>();
  }

  public void Start(ushort listenPort)
  {
    _runtime = new SnapshotRuntime();

    HttpListener listener = new();
    string listeningUrl = $"http://127.0.0.1:{listenPort}/";
    listener.Prefixes.Add(listeningUrl);

    var manager = listener.TimeoutManager;
    manager.IdleConnection = TimeSpan.FromSeconds(5);
    listener.Start();
    Log.Debug($"[Diver] Listening on {listeningUrl}...");
    DispatcherAsync(listener, _cts.Token).GetAwaiter().GetResult();

    Log.Debug("[Diver] Closing listener");
    listener.Stop();
    listener.Close();

    Log.Debug("[Diver] Closing ClrMD runtime and snapshot");
    this.Dispose();

    Log.Debug("[Diver] Exiting");
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

  public static byte[] ReadRequestBody(HttpListenerRequest request) =>
    _cachedRequestBody.Value ?? Array.Empty<byte>();

  public static T DeserializeRequest<T>(HttpListenerRequest request) =>
    MessagePackSerializer.Deserialize<T>(_cachedRequestBody.Value);

  #region HTTP Dispatching
  public async Task DispatcherAsync(
    HttpListener listener,
    CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      HttpListenerContext context = null;
      try
      {
        context = await listener.GetContextAsync();
      }
      catch (ObjectDisposedException)
      {
        Log.Debug("[Diver][Dispatcher] Listener was disposed. Exiting.");
        break;
      }
      catch (HttpListenerException e)
      {
        if (e.Message.StartsWith("The I/O operation has been aborted"))
        {
          Log.Debug("[Diver][Dispatcher] Listener was aborted. Exiting.");
          break;
        }
        Log.Error("[Diver][Dispatcher] HttpListenerException", e);
        continue;
      }
      catch (Exception ex)
      {
        Log.Error("[Diver][Dispatcher] Error in dispatcher loop", ex);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        continue;
      }

      _ = SyncThread.EnqueueAsync(() =>
        HandleDispatchedRequestAsync(context, cancellationToken),
            timeout: TimeSpan.FromSeconds(30));
    }

    Log.Debug("[Diver] HTTP Loop ended. Cleaning up");

    Log.Debug("[Diver] Removing all event subscriptions and hooks");
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
    Log.Debug("[Diver] Removed all event subscriptions and hooks");
  }

  private async Task HandleDispatchedRequestAsync(
    HttpListenerContext requestContext,
    CancellationToken cancellationToken)
  {
    HttpListenerRequest request = requestContext.Request;
    var response = requestContext.Response;

    // Read request body as binary
    using (var ms = new MemoryStream())
    {
      await request.InputStream.CopyToAsync(ms).ConfigureAwait(false);
      _cachedRequestBody.Value = ms.ToArray();
    }

    byte[] responseBody;
    if (_responseBodyCreators.TryGetValue(
      request.Url.AbsolutePath,
      out var respBodyGenerator))
    {
      bool forceUIThread = request.QueryString["ui_thread"] == "true";

      try
      {
        if (forceUIThread)
        {
          var cachedBody = _cachedRequestBody.Value;
          responseBody = STAThread.Execute(() =>
          {
            _cachedRequestBody.Value = cachedBody;
            return respBodyGenerator(request);
          });
        }
        else
        {
          responseBody = respBodyGenerator(request);
        }
      }
      catch (Exception ex)
      {
        Log.Error("[Diver] Exception occurred in handler.", ex);
        Log.Debug($"[Diver] Stack trace: {ex.Message}\n{ex.StackTrace}");
        responseBody = QuickError(ex.Message, ex.StackTrace);
      }
    }
    else
    {
      responseBody = QuickError("Unknown Command");
    }

    response.ContentLength64 = responseBody.Length;
    response.ContentType = MsgPackContentType;

    try
    {
      await response.OutputStream.WriteAsync(responseBody, 0, responseBody.Length, cancellationToken);
    }
    catch (Exception ex)
    {
      Log.Error("[Diver] Failed to write response", ex);
    }
    finally
    {
      response.OutputStream.Close();
    }
  }
  #endregion

  public void Dispose()
  {
    _cts.Cancel();
    _cts.Dispose();
    _runtime?.Dispose();
    _clientCallbacks.Clear();
    STAThread.Stop();
    ReverseCommunicator.Dispose();
  }
}
