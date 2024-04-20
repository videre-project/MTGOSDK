/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System.IO;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;

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
  public bool HasActiveCallbacks => _tokensToEventHandlers.Count > 0;

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
      _listenTask = Task.Run(() => Dispatcher(_listener), _src.Token);
      _listenTask = Task.Factory.StartNew(() =>
          Dispatcher(_listener),
          _src.Token,
          TaskCreationOptions.AttachedToParent,
          TaskScheduler.Default);
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

  private void Dispatcher(HttpListener listener)
  {
    while (_src != null && !_src.IsCancellationRequested)
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
          return;
        }
        catch (System.Net.HttpListenerException)
        {
          // Sometimes happen at teardown. Maybe there's a race condition here and waiting on something
          // can prevent this but I don't really care
          return;
        }

        try
        {
          HandleDispatchedRequest(context);
        }
        catch
        {
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
          // Async event still awaiting new HTTP requests... It's a good time to check
          // if we were signalled to die
          if (_src.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
          {
            // Time to die.
            // Leaving the inner loop will get us to the outter loop where _src is checked (again)
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
      if (_tokensToEventHandlers.TryGetValue(res.Token, out LocalEventCallback callbackFunction))
      {
        (bool voidReturnType, ObjectOrRemoteAddress callbackRes) = callbackFunction(res.Parameters.ToArray());

        InvocationResults ir = new()
        {
          VoidReturnType = voidReturnType,
          ReturnedObjectOrAddress = voidReturnType ? null : callbackRes
        };

        body = JsonConvert.SerializeObject(ir);
      }
      else
      {
        // TODO: I'm not sure the usage of 'DiverError' here is good.
        //       It's sent from the Communicator's side to the Diver's side...
        DiverError errResults = new("Unknown Token", String.Empty);
        body = JsonConvert.SerializeObject(errResults);
      }
    }
    else
    {
      // TODO: I'm not sure the usage of 'DiverError' here is good.
      //       It's sent from the Communicator's side to the Diver's side...
      DiverError errResults = new("Unknown path in URL", String.Empty);
      body = JsonConvert.SerializeObject(errResults);
    }

    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
    // Get a response stream and write the response to it.
    response.ContentLength64 = buffer.Length;
    response.ContentType = "application/json";
    Stream output = response.OutputStream;
    output.Write(buffer, 0, buffer.Length);
    // You must close the output stream.
    output.Close();
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
}
