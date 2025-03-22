/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using static MTGOSDK.Core.Remoting.Interop.DiverCommunicator;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Listens for remote event callback invocations from the Diver
/// </summary>
public class CallbacksListener
{
  private HttpListener _listener = null;
  private Task _listenTask = null;
  private CancellationTokenSource _src = null;

  public IPAddress IP { get; set; }
  public int Port { get; set; }

  private readonly JsonSerializerSettings _withErrors = new()
  {
    MissingMemberHandling = MissingMemberHandling.Error,
  };
  private readonly Dictionary<int, LocalEventCallback> _tokensToEventHandlers = new();
  private readonly Dictionary<LocalEventCallback, int> _eventHandlersToToken = new();

  private readonly Dictionary<int, LocalHookCallback> _tokensToHookCallbacks = new();
  private readonly Dictionary<LocalHookCallback, int> _hookCallbacksToTokens = new();

  private readonly DiverCommunicator _communicator;

  public CallbacksListener(DiverCommunicator communicator)
  {
    _communicator = communicator;
    // Generate a random port with a temporary TcpListener
    int GetRandomUnusedPort()
    {
      var listener = new TcpListener(IPAddress.Any, 0);
      listener.Start();
      var port = ((IPEndPoint)listener.LocalEndpoint).Port;
      listener.Stop();
      return port;
    }

    Port = GetRandomUnusedPort();
    IP = IPAddress.Parse("127.0.0.1");
  }

  public bool IsOpen { get; private set; }
  public bool HasActiveCallbacks =>
    _tokensToEventHandlers.Count + _tokensToHookCallbacks.Count > 0;

  public void Open()
  {
    if (!IsOpen)
    {
      // Need to create HTTP listener and send the Diver it's info
      _listener = new HttpListener();
      string listeningUrl = $"http://{IP}:{Port}/";
      _listener.Prefixes.Add(listeningUrl);
      _listener.Start();
      _src = new CancellationTokenSource();
      _listenTask = Task.Run(async () => await Dispatcher(_listener), _src.Token);
      IsOpen = true;
    }
  }

  public void Close()
  {
    if (IsOpen)
    {
      _src.Cancel();
      try
      {
        _listenTask.Wait();
      }
      catch { }
      _listener.Close();
      _src = null;
      _listener = null;
      _listenTask = null;

      IsOpen = false;
    }
  }

  private readonly SemaphoreSlim _semaphore = new(2 * Environment.ProcessorCount);

  private async Task Dispatcher(HttpListener listener)
  {
    while (_src != null && !_src.IsCancellationRequested)
    {
      try
      {
        HttpListenerContext context = await listener.GetContextAsync();
        await _semaphore.WaitAsync();
        _ = HandleDispatchedRequestAsync(context).ContinueWith(_ => _semaphore.Release());
      }
      catch (HttpListenerException e)
      {
        if (e.ErrorCode == 995)
        {
          // The listener was closed
          break;
        }
        else
        {
          Log.Error($"HttpListenerException: {e}");
        }
      }
      catch (Exception e)
      {
        Log.Error($"Exception: {e}");
      }
    }
  }

  private async Task HandleDispatchedRequestAsync(HttpListenerContext context)
  {
    HttpListenerRequest request = context.Request;
    HttpListenerResponse response = context.Response;

    string body = null;

    if (request.Url.AbsolutePath == "/ping")
    {
      string pongRes = "{\"status\":\"pong\"}";
      byte[] pongResBytes = Encoding.UTF8.GetBytes(pongRes);

      response.ContentLength64 = pongResBytes.Length;
      response.ContentType = "application/json";
      await response.OutputStream.WriteAsync(pongResBytes);
      response.Close();

      return;
    }

    if (request.Url.AbsolutePath == "/invoke_callback")
    {
      using (StreamReader sr = new(request.InputStream))
      {
        body = await sr.ReadToEndAsync();
      }
      var res = JsonConvert.DeserializeObject<CallbackInvocationRequest>(body, _withErrors);
      context.Response.StatusCode = (int)HttpStatusCode.OK;
      context.Response.Close();

      //
      // Set the timestamp for the sender to the callback timestamp as we've
      // already pinned the sender object before processing the callback, and
      // thus no additional processing was required to obtain the timestamp.
      //
      res.Parameters[0].Timestamp = res.Timestamp;

      if (_tokensToEventHandlers.TryGetValue(res.Token, out LocalEventCallback callback))
      {
        //
        // If the callback is an event handler w/ event args, they will also
        // need to have their timestamps set to the callback timestamp as well.
        //
        if (res.Parameters.Count > 1)
        {
          res.Parameters[1].Timestamp = res.Timestamp;
        }

        callback([.. res.Parameters]);
      }
      else if (_tokensToHookCallbacks.TryGetValue(res.Token, out LocalHookCallback hook))
      {
        hook(new HookContext(res.Timestamp),
             res.Parameters.FirstOrDefault(),
             [.. res.Parameters.Skip(1)]);
      }
      return;
    }
  }

  public void EventSubscribe(LocalEventCallback callback, int token)
  {
    _tokensToEventHandlers[token] = callback;
    _eventHandlersToToken[callback] = token;
  }

  public int EventUnsubscribe(LocalEventCallback callback)
  {
    if (_eventHandlersToToken.TryGetValue(callback, out int token))
    {
      _tokensToEventHandlers.Remove(token);
      _eventHandlersToToken.Remove(callback);
      return token;
    }
    else
    {
      throw new Exception($"[CallbackListener] EventUnsubscribe TryGetValue failed");
    }
  }

  public void HookSubscribe(LocalHookCallback callback, int token)
  {
    _tokensToHookCallbacks[token] = callback;
    _hookCallbacksToTokens[callback] = token;
  }

  public int HookUnsubscribe(LocalHookCallback callback)
  {
    if (_hookCallbacksToTokens.TryGetValue(callback, out int token))
    {
      _tokensToHookCallbacks.Remove(token);
      _hookCallbacksToTokens.Remove(callback);
      return token;
    }
    else
    {
      throw new Exception($"[CallbackListener] HookUnsubscribe TryGetValue failed");
    }
  }
}
