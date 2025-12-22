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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using MTGOSDK.Core;
using MTGOSDK.Core.Memory.Snapshot;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

using ScubaDiver.Hooking;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  // Runtime analysis and exploration fields
  private SnapshotRuntime _runtime;

  // HTTP Responses fields
  private readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

  // Cached request body for STA thread retries (body can only be read once from InputStream)
  // Using AsyncLocal so the value flows to the STA worker thread during Execute()
  private static readonly AsyncLocal<string> _cachedRequestBody = new();

  // Callbacks Endpoint of the Controller process
  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
  private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;

  private readonly CancellationTokenSource _cts = new();

  private readonly ConcurrentDictionary<int, HashSet<int>> _clientCallbacks = new();
  private readonly ConcurrentDictionary<int, CancellationTokenSource> _callbackTokens = new();

  public Diver()
  {
    _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
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
    Log.Debug($"[Diver] Listening on {listeningUrl}...");
    DispatcherAsync(listener, _cts.Token).GetAwaiter().GetResult();

    Log.Debug("[Diver] Closing listener");
    listener.Stop();
    listener.Close();

    Log.Debug("[Diver] Closing ClrMD runtime and snapshot");
    this.Dispose();

    Log.Debug("[Diver] Exiting");
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

  /// <summary>
  /// Returns the pre-cached request body. The body is cached in the dispatcher
  /// before handlers run to support STA thread retries.
  /// </summary>
  public static string ReadRequestBody(HttpListenerRequest request)
  {
    return _cachedRequestBody.Value ?? string.Empty;
  }

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
          Log.Debug($"[Diver][Dispatcher] Listener was aborted. Exiting.");
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

      // Use SyncThread to control concurrency and backpressure, with a 30s timeout
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

    // Pre-read and cache the request body before any handler runs.
    // This is necessary because InputStream can only be read once, and we may
    // need to retry the handler on the STA thread after the first attempt fails.
    using (StreamReader sr = new(request.InputStream))
    {
      _cachedRequestBody.Value = sr.ReadToEnd();
    }

    string body;
    if (_responseBodyCreators.TryGetValue(
      request.Url.AbsolutePath,
      out var respBodyGenerator))
    {
      // Check if the client explicitly requested UI thread execution
      bool forceUIThread = request.QueryString["ui_thread"] == "true";

      try
      {
        if (forceUIThread)
        {
          // Execute directly on UI thread (no retry needed)
          var cachedBody = _cachedRequestBody.Value;
          body = STAThread.Execute(() =>
          {
            _cachedRequestBody.Value = cachedBody;
            return respBodyGenerator(request);
          });
        }
        else
        {
          body = respBodyGenerator(request);
        }
      }
      catch (Exception ex)
      {
        Log.Error("[Diver] Exception occurred in handler.", ex);
        Log.Debug("[Diver] Stack trace: " + ex.Message + "\n" + ex.StackTrace);
        body = QuickError(ex.Message, ex.StackTrace);
      }
    }
    else
    {
      body = QuickError("Unknown Command");
    }

    byte[] buffer = Encoding.UTF8.GetBytes(body);
    response.ContentLength64 = buffer.Length;
    response.ContentType = "application/json";

    try
    {
      await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
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

  // IDisposable
  public void Dispose()
  {
    _cts.Cancel();
    _cts.Dispose();
    _runtime?.Dispose();
    _clientCallbacks.Clear();
    STAThread.Stop(); // Cleanup the STA worker thread.
    ReverseCommunicator.Dispose(); // Cleanup the shared HttpClient instance.
  }
}
