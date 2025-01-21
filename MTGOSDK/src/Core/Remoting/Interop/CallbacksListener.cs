/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Net;
using System.Net.Sockets;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions;
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

  private async Task Dispatcher(HttpListener listener)
  {
    while (_src != null && !_src.IsCancellationRequested)
    {
      try
      {
        HttpListenerContext context = await listener.GetContextAsync();
        HandleDispatchedRequest(context);
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

  private void HandleDispatchedRequest(HttpListenerContext context)
  {
    HttpListenerRequest request = context.Request;

    var response = context.Response;
    string body = null;

    if (request.Url.AbsolutePath == "/ping")
    {
      string pongRes = "{\"status\":\"pong\"}";
      byte[] pongResBytes = System.Text.Encoding.UTF8.GetBytes(pongRes);
      // Get a response stream and write the response to it.
      response.ContentLength64 = pongResBytes.Length;
      response.ContentType = "application/json";
      Stream outputStream = response.OutputStream;
      outputStream.Write(pongResBytes, 0, pongResBytes.Length);
      // You must close the output stream.
      outputStream.Close();
      return;
    }

    if (request.Url.AbsolutePath == "/invoke_callback")
    {
      using (StreamReader sr = new(request.InputStream))
      {
        body = sr.ReadToEnd();
      }
      var res = JsonConvert.DeserializeObject<CallbackInvocationRequest>(body, _withErrors);

      // Send a response back to the Diver immediately
      body = JsonConvert.SerializeObject(new InvocationResults()
      {
        VoidReturnType = true,
        ReturnedObjectOrAddress = null
      });
      byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
      // Get a response stream and write the response to it.
      response.ContentLength64 = buffer.Length;
      response.ContentType = "application/json";
      Stream output = response.OutputStream;
      output.Write(buffer, 0, buffer.Length);
      // You must close the output stream.
      output.Close();

      if (_tokensToEventHandlers.TryGetValue(res.Token, out LocalEventCallback callback))
      {
        // Task.Run(() =>
        //   callback(res.Parameters.ToArray()));

        SyncThread.Enqueue(() =>
            callback(res.Parameters.ToArray()));
      }
      else if (_tokensToHookCallbacks.TryGetValue(res.Token, out LocalHookCallback hook))
      {
        // Task.Run(() =>
        //   hook(new HookContext(res.StackTrace),
        //     res.Parameters.FirstOrDefault(),
        //     res.Parameters.Skip(1).ToArray()));

        SyncThread.Enqueue(() =>
            hook(new HookContext(res.StackTrace),
                res.Parameters.FirstOrDefault(),
                res.Parameters.Skip(1).ToArray()));
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
