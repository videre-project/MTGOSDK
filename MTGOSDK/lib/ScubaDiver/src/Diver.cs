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

using Newtonsoft.Json;

using MTGOSDK.Core;
using MTGOSDK.Core.Compiler.Snapshot;
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

  // Callbacks Endpoint of the Controller process
  private readonly ConcurrentDictionary<int, RegisteredEventHandlerInfo> _remoteEventHandler;
  private readonly ConcurrentDictionary<int, RegisteredMethodHookInfo> _remoteHooks;

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
    Dispatcher(listener);

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

  #region HTTP Dispatching
  private async void HandleDispatchedRequestAsync(HttpListenerContext requestContext)
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
        Log.Error("[Diver] Exception occurred in handler.", ex);
        Log.Debug("[Diver] Stack trace: " + ex.StackTrace);
        body = QuickError(ex.Message, ex.StackTrace);
      }
    }
    else
    {
      body = QuickError("Unknown Command");
    }

    byte[] buffer = Encoding.UTF8.GetBytes(body);

    // Set response properties
    response.ContentLength64 = buffer.Length;
    response.ContentType = "application/json";

    try
    {
      // Write asynchronously to avoid blocking
      await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }
    catch (Exception ex)
    {
      Log.Error("[Diver] Failed to write response", ex);
    }
    finally
    {
      // Always close the output stream
      response.OutputStream.Close();
    }
  }

  private void Dispatcher(HttpListener listener)
  {
    // Using a timeout we can make sure not to block if the
    // 'stayAlive' state changes to "reset" (which means we should die)
    int pendingRequests = 0;

    while (_stayAlive.WaitOne(TimeSpan.FromMilliseconds(100)))
    {
      try
      {
        // Begin accepting a new request - this is non-blocking
        IAsyncResult asyncOperation = listener.BeginGetContext(result =>
        {
          Interlocked.Decrement(ref pendingRequests);

          HttpListener listenerObj = (HttpListener)result.AsyncState;
          HttpListenerContext context;
          try
          {
            context = listenerObj.EndGetContext(result);
          }
          catch (ObjectDisposedException)
          {
            Log.Debug("[Diver][ListenerCallback] Listener was disposed. Exiting.");
            return;
          }
          catch (HttpListenerException e)
          {
            if (e.Message.StartsWith("The I/O operation has been aborted"))
            {
              Log.Debug($"[Diver][ListenerCallback] Listener was aborted. Exiting.");
              return;
            }
            throw;
          }

          try
          {
            HandleDispatchedRequestAsync(context);
          }
          catch (Exception e)
          {
            Log.Debug("[Diver] Request handling failed! Exception: " + e.ToString());
          }
        }, listener);

        Interlocked.Increment(ref pendingRequests);

        // Only sleep if we're accepting requests too quickly
        // This prevents tight CPU looping while still being responsive
        if (pendingRequests > Environment.ProcessorCount * 2)
        {
          Thread.Sleep(10);
        }

        // Check if we should terminate
        if (!_stayAlive.WaitOne(0))
          break;
      }
      catch (Exception ex)
      {
        Log.Error("[Diver] Error in dispatcher loop", ex);
        // Short pause before retry
        Thread.Sleep(100);
      }
    }

    // Wait for pending requests to complete with a timeout
    int timeout = 5000; // 5 seconds max wait
    int waited = 0;
    while (pendingRequests > 0 && waited < timeout)
    {
      Thread.Sleep(100);
      waited += 100;
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
  #endregion

  // IDisposable
  public void Dispose()
  {
    _runtime?.Dispose();
    ReverseCommunicator.Dispose(); // Cleanup the shared HttpClient instance.
  }
}
