/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Compiler.Snapshot;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  // Runtime analysis and exploration fields
  private SnapshotRuntime _runtime;

  // HTTP Responses fields
  private readonly Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

  // Callbacks Endpoint of the Controller process
  private bool _monitorEndpoints = true;
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
      {"/get_item", MakeArrayItemResponse},
      // Callbacks
      {"/event_subscribe", MakeEventSubscribeResponse},
      {"/event_unsubscribe", MakeEventUnsubscribeResponse},
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
    Log.Debug($"[Diver] Listening on {listeningUrl}...");

    Task endpointsMonitor = Task.Run(CallbacksEndpointsMonitor);
    Dispatcher(listener);
    Log.Debug("[Diver] Stopping Callback Endpoints Monitor");
    _monitorEndpoints = false;
    try { endpointsMonitor.Wait(); } catch { }

    Log.Debug("[Diver] Closing listener");
    listener.Stop();
    listener.Close();
    Log.Debug("[Diver] Closing ClrMD runtime and snapshot");

    Log.Debug("[Diver] Unpinning objects");
    Log.Debug("[Diver] Unpinning finished");

    Log.Debug("[Diver] Dispatcher returned, Start is complete.");
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
        Log.Debug($"[Diver] Checking if callback client at {endpoint} is alive. Token = {registeredEventHandlerInfo.Key}. Type = Event");
        bool alive = reverseCommunicator.CheckIfAlive();
        Log.Debug($"[Diver] Callback client at {endpoint} (Token = {registeredEventHandlerInfo.Key}) is alive = {alive}");
        if (!alive)
        {
          Log.Debug(
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
    if (_responseBodyCreators.TryGetValue(request.Url.AbsolutePath, out var respBodyGenerator))
    {
      try
      {
        body = respBodyGenerator(request);
      }
      catch (Exception ex)
      {
        Log.Debug("[Diver] Exception in handler: " + ex.ToString());
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
          HandleDispatchedRequest(context);
        }
        catch (Exception e)
        {
          Log.Debug("[Diver] Task faulted! Exception: " + e.ToString());
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

    Log.Debug("[Diver] HTTP Loop ended. Cleaning up");

    Log.Debug("[Diver] Removing all event subscriptions");
    foreach (RegisteredEventHandlerInfo rehi in _remoteEventHandler.Values)
    {
      rehi.EventInfo.RemoveEventHandler(rehi.Target, rehi.RegisteredProxy);
    }
    _remoteEventHandler.Clear();
    Log.Debug("[Diver] Removed all event subscriptions");
  }
  #endregion

  // IDisposable
  public void Dispose()
  {
    _runtime?.Dispose();
  }
}
