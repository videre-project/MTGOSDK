/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using MessagePack;

using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;


namespace ScubaDiver;

/// <summary>
/// TCP server for Diver that handles multiplexed requests via SyncThread.
/// Supports multiple concurrent clients using System.IO.Pipelines.
/// </summary>
public class TcpServer : IDisposable
{
  private readonly TcpListener _listener;
  private int _disposed;
  private readonly CancellationTokenSource _cts;
  private readonly Func<string, byte[], byte[]> _requestHandler;

  // Track active client connections
  private readonly ConcurrentDictionary<Guid, TcpClientConnection> _clients = new();

  /// <summary>
  /// The port the server is listening on.
  /// </summary>
  public int Port { get; }

  /// <summary>
  /// The number of currently connected clients.
  /// </summary>
  public int ClientCount => _clients.Count;

  public TcpServer(
    int port,
    Func<string, byte[], byte[]> requestHandler,
    CancellationTokenSource cancellationTokenSource = null)
  {
    Port = port;
    _requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
    _cts = cancellationTokenSource ?? new CancellationTokenSource();
    _listener = new TcpListener(IPAddress.Loopback, port);
    
    // Allow rebinding to the same port after unclean shutdown
    _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    _listener.ExclusiveAddressUse = false;
  }

  /// <summary>
  /// Accepts clients until cancellation or disposal stops the listener.
  /// Each accepted connection handles requests independently.
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (Volatile.Read(ref _disposed) != 0)
      throw new ObjectDisposedException(nameof(TcpServer));

    using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
    var token = shutdown.Token;
    _listener.Start();
    using var stopListening = token.Register(_listener.Stop);
    Log.Debug($"[TcpServer] Listening on port {Port}...");

    try
    {
      // Loop to accept new connections
      while (!token.IsCancellationRequested)
      {
        // Wait for a client to connect
        TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        
        // Handle client in a background task (fire-and-forget from acceptor's perspective)
        _ = HandleNewClientAsync(client, token);
      }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    catch (SocketException) when (token.IsCancellationRequested) { }
    catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
    catch (Exception ex)
    {
      Log.Error($"[TcpServer] Server error: {ex.Message}");
    }
    finally
    {
      _listener.Stop();
    }
  }

  private async Task HandleNewClientAsync(TcpClient client, CancellationToken token)
  {
    var connectionId = Guid.NewGuid();
    var connection = new TcpClientConnection(client, _requestHandler);

    try
    {
      if (_clients.TryAdd(connectionId, connection))
      {
        Log.Debug($"[TcpServer] Client {connectionId} connected. Active clients: {_clients.Count}");
        
        // Run the connection loop (this will block until client disconnects)
        await connection.RunAsync(token).ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      Log.Error($"[TcpServer] Error handling client {connectionId}: {ex.Message}");
    }
    finally
    {
      _clients.TryRemove(connectionId, out _);
      connection.Dispose();
      Log.Debug($"[TcpServer] Client {connectionId} disconnected. Active clients: {_clients.Count}");
    }
  }

  /// <summary>
  /// Sends a callback to all connected clients (broadcast).
  /// </summary>
  public void SendCallback(CallbackInvocationRequest callback)
  {
    if (_clients.IsEmpty) return;

    try
    {
      var body = MessagePackSerializer.Serialize(callback);
      
      foreach (var client in _clients.Values)
      {
        client.SendCallback(body);
      }
    }
    catch (Exception ex)
    {
      Log.Error($"[TcpServer] Failed to broadcast callback: {ex.Message}");
    }
  }

  /// <summary>
  /// Disposes managed resources.
  /// </summary>
  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    _cts.Cancel();
    try { _listener.Stop(); } catch { }
    
    foreach (var client in _clients.Values)
    {
      client.Dispose();
    }
    _clients.Clear();
    
    _cts.Dispose();
  }

  /// <summary>
  /// Inner class handling a single TCP client connection using Pipelines.
  /// </summary>
  private class TcpClientConnection : TcpPipelineBase
  {
    private readonly TcpClient _client;
    private readonly Func<string, byte[], byte[]> _requestHandler;
    private NetworkStream _stream;
    private Task _writerTask;

    public TcpClientConnection(TcpClient client, Func<string, byte[], byte[]> requestHandler)
    {
      _client = client;
      _requestHandler = requestHandler;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
      try
      {
        _client.NoDelay = true;
        _client.ReceiveBufferSize = 64 * 1024;
        _client.SendBufferSize = 64 * 1024;

        _stream = _client.GetStream();
        
        // Initialize Pipelines from base class
        InitializePipelines(_stream);
        _isConnected = true;

        // Start writer loop (drains the write channel)
        _writerTask = WriterLoopAsync(cancellationToken);

        // Run reader loop (blocks until disconnect)
        await ReaderLoopAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Log.Error($"[TcpClientConnection] Connection error: {ex.Message}");
      }
      finally
      {
        _isConnected = false;
        _writeChannel.Writer.TryComplete();
        if (_writerTask != null)
        {
          try { await _writerTask.ConfigureAwait(false); } catch { }
        }
        CompletePipelines(); // Cleanup pipelines
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
      }
    }

    public void SendCallback(byte[] serializedCallback)
    {
      if (!_isConnected) return;
      
      // Enqueue to write channel (fire-and-forget)
      if (!TryEnqueueWrite(0, TcpMessageType.Callback, "/invoke_callback", serializedCallback))
        Log.Warning("[TcpClientConnection] Callback could not be queued because the write channel is full or closed.");
    }

    /// <summary>
    /// Reader loop that processes incoming requests via SyncThread.
    /// </summary>
    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
      try
      {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
          ReadResult result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
          ReadOnlySequence<byte> buffer = result.Buffer;

          while (TryParseFrame(ref buffer, out var frame))
          {
            if (frame.Type == TcpMessageType.Request)
            {
              // Capture the correlation ID for the scheduled request.
              var mid = frame.MessageId;
              _ = SyncThread.EnqueueAsync(async () =>
              {
                LogContext.MessageId.Value = mid;
                try
                {
                  await ProcessRequestAsync(mid, frame.Endpoint, frame.Body, cancellationToken)
                    .ConfigureAwait(false);
                }
                finally
                {
                  LogContext.MessageId.Value = null;
                }
              });
            }
          }

          _pipeReader.AdvanceTo(buffer.Start, buffer.End);

          if (result.IsCompleted) break;
        }
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Log.Error($"[TcpClientConnection] Reader loop error: {ex.Message}");
      }
      finally
      {
        await _pipeReader.CompleteAsync().ConfigureAwait(false);
      }
    }

    private async Task ProcessRequestAsync(int messageId, string endpoint, byte[] body, CancellationToken cancellationToken)
    {
      try
      {
        byte[] responseBody = _requestHandler(endpoint, body);

        await EnqueueWriteAsync(messageId, TcpMessageType.Response, endpoint, responseBody, cancellationToken)
          .ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Log.Error($"[TcpClientConnection] Error processing request {messageId}: {ex.Message}");
        try
        {
          var errorResponse = new DiverResponse<object>
          {
            IsError = true,
            ErrorMessage = ex.Message,
            ErrorStackTrace = ex.StackTrace
          };
          var errorBody = MessagePackSerializer.Serialize(errorResponse);
          await EnqueueWriteAsync(messageId, TcpMessageType.Response, endpoint, errorBody, cancellationToken)
            .ConfigureAwait(false);
        }
        catch { }
      }
    }
  }
}
