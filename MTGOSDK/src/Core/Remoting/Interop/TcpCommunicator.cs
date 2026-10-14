/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Low-allocation multiplexed TCP communicator for SDK ↔ Diver IPC.
/// Supports concurrent requests with out-of-order responses and fire-and-forget callbacks.
/// Uses System.IO.Pipelines for zero-copy I/O.
/// </summary>
/// <remarks>
/// Each connection is a separate <see cref="Session"/> with its own pipes,
/// write queue and pending requests, so after a reconnect nothing of a
/// dropped connection (a late reader, a queued write or a request in
/// flight) can reach or fail the new one.
/// </remarks>
public class TcpCommunicator : TcpPipelineBase
{
  private readonly string _hostname;
  private readonly int _port;
  private readonly CancellationTokenSource _cts;

  // Serialises connecting, so concurrent callers open one connection.
  private readonly SemaphoreSlim _connectGate = new(1, 1);
  private volatile Session? _session;

  // Request ids stay unique across connections.
  private int _nextRequestId = 0;
  private volatile bool _supportsPinOwnership;
  private int _disposed;

  internal bool SupportsPinOwnership => _supportsPinOwnership;

  // Callback handling, in wire order across reconnects.
  private readonly ChannelScheduler _callbackScheduler;
  private Action<string, byte[]> _callbackHandler;

  /// <summary>
  /// Whether the connection is active and client is connected.
  /// </summary>
  public new bool IsConnected => _session?.IsOpen == true;

  /// <summary>
  /// The number of IPC requests currently in-flight (awaiting response).
  /// </summary>
  public int PendingRequestCount => _session?.PendingCount ?? 0;

  public TcpCommunicator(
    string hostname,
    int port,
    CancellationTokenSource cancellationTokenSource = null)
  {
    _hostname = hostname;
    _port = port;
    _cts = cancellationTokenSource ?? new CancellationTokenSource();
    // Run callbacks in wire order on a separate queue so the reader can complete
    // responses even when a callback is waiting for a reflection request.
    _callbackScheduler = new ChannelScheduler(1);
  }

  /// <summary>
  /// Registers a handler for incoming callbacks from the Diver.
  /// </summary>
  public void SetCallbackHandler(Action<string, byte[]> handler) =>
    _callbackHandler = handler;

  /// <summary>
  /// Connects to the Diver and starts the background reader.
  /// </summary>
  public Task ConnectAsync(CancellationToken cancellationToken = default) =>
    ConnectAsync(null, cancellationToken);

  /// <summary>
  /// Connects to the Diver (unless connected) and runs
  /// <paramref name="handshake"/> on the new connection before any other
  /// request can be sent on it. The handshake sends its requests with the
  /// function it is given. If it fails, or <paramref name="cancellationToken"/>
  /// is cancelled before the connection is ready, the connection is closed
  /// and the error is thrown. The token does not outlive the attempt.
  /// </summary>
  public async Task ConnectAsync(
    Func<Func<string, byte[], Task>, Task>? handshake,
    CancellationToken cancellationToken = default)
  {
    await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (Volatile.Read(ref _disposed) != 0)
        throw new ObjectDisposedException(nameof(TcpCommunicator));
      if (IsConnected) return;

      // A dropped connection ends before the new one starts: its pending
      // requests fail and its queued writes are dropped.
      var previous = _session;
      _session = null;
      if (previous != null) await previous.CloseAsync().ConfigureAwait(false);

      var session = new Session(this);
      await session.OpenAsync(_hostname, _port, cancellationToken).ConfigureAwait(false);
      // Published now; other requests wait until the handshake is done.
      _session = session;

      try
      {
        if (handshake != null)
        {
          await handshake((endpoint, body) =>
            SendAsync<object>(session, endpoint, body, beforeReady: true, cancellationToken))
            .ConfigureAwait(false);
        }
        // A handshake can complete without observing the token; a cancelled
        // attempt still closes its connection.
        cancellationToken.ThrowIfCancellationRequested();
        session.MarkReady();
      }
      catch (Exception ex)
      {
        _session = null;
        await session.CloseAsync(ex).ConfigureAwait(false);
        throw;
      }
      Log.Debug("[TcpCommunicator] Connected to {host}:{port}", _hostname, _port);
    }
    finally
    {
      _connectGate.Release();
    }
  }

  /// <summary>
  /// Sends a request and waits for the response.
  /// </summary>
  public Task<T> SendRequestAsync<T>(
    string endpoint,
    byte[] body,
    CancellationToken cancellationToken = default)
  {
    var session = _session;
    if (session == null || !session.IsOpen)
      throw new InvalidOperationException("Not connected");
    return SendAsync<T>(session, endpoint, body, beforeReady: false, cancellationToken);
  }

  /// <summary>
  /// Sends a request without expecting a typed response (void operations).
  /// </summary>
  public async Task SendRequestAsync(
    string endpoint,
    byte[] body,
    CancellationToken cancellationToken = default)
  {
    await SendRequestAsync<object>(
      endpoint,
      body ?? Array.Empty<byte>(),
      cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Sends a request on one connection and decodes its response. The request
  /// is bound to that connection: if it closes, the request fails and is
  /// never sent on another one.
  /// </summary>
  private async Task<T> SendAsync<T>(
    Session session,
    string endpoint,
    byte[] body,
    bool beforeReady,
    CancellationToken cancellationToken)
  {
    int requestId = Interlocked.Increment(ref _nextRequestId);

    // Tag all logs emitted during this request with the correlation ID
    LogContext.MessageId.Value = requestId;
    try
    {
      byte[] responseBytes = await session
        .SendAsync(requestId, endpoint, body ?? Array.Empty<byte>(), beforeReady, cancellationToken)
        .ConfigureAwait(false);

      // Response body is directly the DiverResponse (no TcpMessage wrapper)
      var wrapper = MessagePackSerializer.Deserialize<DiverResponse<T>>(responseBytes);
      if (wrapper.IsError)
      {
        var errorMsg = string.IsNullOrEmpty(wrapper.ErrorStackTrace)
          ? wrapper.ErrorMessage
          : $"{wrapper.ErrorMessage}\n\nRemote Stack Trace:\n{wrapper.ErrorStackTrace}";
        throw new Core.Exceptions.ExternalErrorException(errorMsg);
      }

      if (wrapper.PinOwner is not null) _supportsPinOwnership = true;
      if (wrapper.Data is IPinResponse pins)
        pins.PinOwner = wrapper.PinOwner;
      return wrapper.Data;
    }
    finally
    {
      LogContext.MessageId.Value = null;
    }
  }

  private void DispatchCallback(string endpoint, byte[] body) =>
    //
    // Dispatch callback (fire-and-forget)
    //
    // Offload to thread pool to prevent blocking the reader loop
    // which is needed to handle nested IPC calls (e.g. DumpType)
    //
    _callbackScheduler.Enqueue(() =>
    {
      try
      {
        _callbackHandler?.Invoke(endpoint, body);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "[TcpCommunicator] Callback handler error");
      }
    });

  /// <summary>
  /// Disposes managed resources.
  /// </summary>
  public override void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    _cts.Cancel();
    _callbackScheduler.Dispose();

    // Wait for the connection's loops, then close it.
    var session = _session;
    _session = null;
    try { session?.CloseAsync().Wait(TimeSpan.FromSeconds(4)); } catch { }

    // Dispose base class (completes pipelines)
    base.Dispose();

    _cts.Dispose();
  }

  /// <summary>
  /// One connection to the Diver: its socket, pipes, write queue, pending
  /// requests and loops.
  /// </summary>
  private sealed class Session(TcpCommunicator owner) : TcpPipelineBase
  {
    private readonly CancellationTokenSource _cts =
      CancellationTokenSource.CreateLinkedTokenSource(owner._cts.Token);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pending = new();
    private readonly TaskCompletionSource<bool> _ready =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stateLock = new();
    private bool _closed;
    private Task? _closing;

    private TcpClient _client;
    private NetworkStream _stream;
    private Task _readerTask = Task.CompletedTask;
    private Task _writerTask = Task.CompletedTask;

    public bool IsOpen => _isConnected && _client?.Connected == true;
    public int PendingCount => _pending.Count;

    public async Task OpenAsync(string hostname, int port, CancellationToken cancellationToken)
    {
      _client = new TcpClient
      {
        NoDelay = true, // Disable Nagle's algorithm for low latency
        ReceiveBufferSize = 64 * 1024,
        SendBufferSize = 64 * 1024
      };

      try
      {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        // Cancelling abandons the attempt by closing the socket.
        using (attempt.Token.Register(() => _client.Dispose()))
          await _client.ConnectAsync(hostname, port).ConfigureAwait(false);
        attempt.Token.ThrowIfCancellationRequested();
        _stream = _client.GetStream();
      }
      catch
      {
        _client.Dispose();
        throw;
      }

      InitializePipelines(_stream);
      _isConnected = true;

      Log.Debug("[TcpCommunicator] Starting reader and writer loops");
      _readerTask = ReaderLoopAsync(_cts.Token);
      _writerTask = WriterLoopAsync(_cts.Token);
    }

    public void MarkReady() => _ready.TrySetResult(true);

    public async Task<byte[]> SendAsync(
      int requestId,
      string endpoint,
      byte[] body,
      bool beforeReady,
      CancellationToken cancellationToken)
    {
      // Only the handshake runs before the connection is ready.
      if (!beforeReady)
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

      var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
      lock (_stateLock)
      {
        if (_closed) throw new InvalidOperationException("Not connected");
        _pending[requestId] = tcs;
      }

      try
      {
        // Use cancellation token
        using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        {
          // Enqueue on this connection's own queue: once it is closed the
          // write fails here instead of reaching another connection.
          try
          {
            await EnqueueWriteAsync(requestId, TcpMessageType.Request, endpoint, body, cancellationToken)
              .ConfigureAwait(false);
          }
          catch (ChannelClosedException ex)
          {
            throw new InvalidOperationException("Not connected", ex);
          }

          // Wait for response
          return await tcs.Task.ConfigureAwait(false);
        }
      }
      finally
      {
        _pending.TryRemove(requestId, out _);
      }
    }

    /// <summary>
    /// Background reader loop that correlates responses and dispatches callbacks.
    /// Uses PipeReader for zero-copy reads.
    /// </summary>
    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
      try
      {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
          // Read from PipeReader (zero-copy from socket buffers)
          ReadResult result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
          ReadOnlySequence<byte> buffer = result.Buffer;

          // Process all complete frames in the buffer
          while (TryParseFrame(ref buffer, out var frame))
          {
            switch (frame.Type)
            {
              case TcpMessageType.Response:
                // Correlate response with pending request
                if (_pending.TryRemove(frame.MessageId, out var tcs))
                {
                  tcs.TrySetResult(frame.Body);
                }
                else
                {
                  Log.Warning($"[TcpCommunicator] No pending request for response {frame.MessageId}");
                }
                break;

              case TcpMessageType.Callback:
                owner.DispatchCallback(frame.Endpoint, frame.Body);
                break;
            }
          }

          // Tell PipeReader how much we consumed
          _pipeReader.AdvanceTo(buffer.Start, buffer.End);

          // Check for completion
          if (result.IsCompleted)
            break;
        }
      }
      catch (OperationCanceledException)
      {
        // Expected on shutdown
      }
      catch (Exception ex)
      {
        if (!cancellationToken.IsCancellationRequested)
          Log.Error(ex, "[TcpCommunicator] Reader loop error");
      }
      finally
      {
        MarkClosed(null);
      }
    }

    /// <summary>
    /// Stops accepting requests and fails this connection's pending ones (and
    /// a handshake still waited for).
    /// </summary>
    private void MarkClosed(Exception? error)
    {
      lock (_stateLock)
      {
        _closed = true;
        _isConnected = false;
      }
      _writeChannel.Writer.TryComplete();
      if (error != null) _ready.TrySetException(error);
      else _ready.TrySetCanceled();

      // Fail all pending requests
      foreach (var kvp in _pending)
      {
        kvp.Value.TrySetCanceled();
      }
      _pending.Clear();
    }

    /// <summary>
    /// Closes this connection: requests fail, the socket is closed and its
    /// loops are awaited for a bounded time (a loop still running after that
    /// only touches this connection).
    /// </summary>
    public Task CloseAsync(Exception? error = null)
    {
      lock (_stateLock)
      {
        _closing ??= CloseCoreAsync(error);
        return _closing;
      }
    }

    private async Task CloseCoreAsync(Exception? error)
    {
      MarkClosed(error);
      _cts.Cancel();
      try { _stream?.Dispose(); } catch { }
      try { _client?.Dispose(); } catch { }

      var loops = Task.WhenAll(_readerTask, _writerTask);
      if (await Task.WhenAny(loops, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) != loops)
        Log.Warning("[TcpCommunicator] A closed connection's loops did not end in time.");
      CompletePipelines();
      _cts.Dispose();
    }
  }
}
