/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Buffers;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;

using MessagePack;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Low-allocation multiplexed TCP communicator for SDK ↔ Diver IPC.
/// Supports concurrent requests with out-of-order responses and fire-and-forget callbacks.
/// Uses System.IO.Pipelines for zero-copy I/O.
/// </summary>
public class TcpCommunicator : TcpPipelineBase
{
  private readonly string _hostname;
  private readonly int _port;
  private readonly CancellationTokenSource _cts;

  private TcpClient _client;
  private NetworkStream _stream;
  private Task _readerTask;
  private Task _writerTask;

  // Request/response correlation
  private int _nextRequestId = 0;
  private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pendingRequests = new();

  // Callback handling
  private Action<string, byte[]> _callbackHandler;

  /// <summary>
  /// Whether the connection is active and client is connected.
  /// </summary>
  public new bool IsConnected => _isConnected && _client?.Connected == true;

  public TcpCommunicator(
    string hostname,
    int port,
    CancellationTokenSource cancellationTokenSource = null)
  {
    _hostname = hostname;
    _port = port;
    _cts = cancellationTokenSource ?? new CancellationTokenSource();
  }

  /// <summary>
  /// Registers a handler for incoming callbacks from the Diver.
  /// </summary>
  public void SetCallbackHandler(Action<string, byte[]> handler)
  {
    _callbackHandler = handler;
  }

  /// <summary>
  /// Connects to the Diver and starts the background reader.
  /// </summary>
  public async Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    if (_isConnected) return;

    _client = new TcpClient
    {
      NoDelay = true, // Disable Nagle's algorithm for low latency
      ReceiveBufferSize = 64 * 1024,
      SendBufferSize = 64 * 1024
    };

    await _client.ConnectAsync(_hostname, _port).ConfigureAwait(false);
    _stream = _client.GetStream();

    // Initialize Pipelines from base class
    InitializePipelines(_stream);

    _isConnected = true;

    // Start background tasks
    Log.Debug("[TcpCommunicator] Starting reader and writer loops");
    _readerTask = ReaderLoopAsync(_cts.Token);
    _writerTask = WriterLoopAsync(_cts.Token);
    Log.Debug("[TcpCommunicator] Connected to {host}:{port}", _hostname, _port);
  }

  /// <summary>
  /// Sends a request and waits for the response.
  /// </summary>
  public async Task<T> SendRequestAsync<T>(
    string endpoint,
    byte[] body,
    CancellationToken cancellationToken = default)
  {
    if (!_isConnected)
      throw new InvalidOperationException("Not connected");

    int requestId = Interlocked.Increment(ref _nextRequestId);
    var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

    _pendingRequests[requestId] = tcs;

    try
    {
      // Use cancellation token
      using (cancellationToken.Register(() => tcs.TrySetCanceled()))
      {
        // Enqueue write via channel (lock-free)
        await EnqueueWriteAsync(requestId, TcpMessageType.Request, endpoint, body ?? Array.Empty<byte>(), cancellationToken)
          .ConfigureAwait(false);

        // Wait for response
        var responseBytes = await tcs.Task.ConfigureAwait(false);

        // Response body is directly the DiverResponse (no TcpMessage wrapper)
        var wrapper = MessagePackSerializer.Deserialize<DiverResponse<T>>(responseBytes);
        if (wrapper.IsError)
        {
          var errorMsg = string.IsNullOrEmpty(wrapper.ErrorStackTrace)
            ? wrapper.ErrorMessage
            : $"{wrapper.ErrorMessage}\n\nRemote Stack Trace:\n{wrapper.ErrorStackTrace}";
          throw new Core.Exceptions.ExternalErrorException(errorMsg);
        }

        return wrapper.Data;
      }
    }
    finally
    {
      _pendingRequests.TryRemove(requestId, out _);
    }
  }

  /// <summary>
  /// Sends a request without expecting a typed response (void operations).
  /// </summary>
  public async Task SendRequestAsync(
    string endpoint,
    byte[] body,
    CancellationToken cancellationToken = default)
  {
    //
    // Wrap request in TracedRequest for distributed tracing
    //
    using var activity = s_activitySource.StartActivity(
      $"IPC Request: {endpoint}", 
      ActivityKind.Client);
    
    // Tag for flow event visualization
    activity?.SetTag("ipc.flow", "start");
    activity?.SetTag("thread.id", Thread.CurrentThread.ManagedThreadId.ToString());

    var tracedReq = new TracedRequest
    {
      TraceParent = activity?.Id ?? Activity.Current?.Id,
      TraceState = activity?.TraceStateString ?? Activity.Current?.TraceStateString,
      Body = body ?? Array.Empty<byte>()
    };
    
    // Serialize wrapper manually
    byte[] wrappedBody = TracedRequest.Serialize(tracedReq);

    await SendRequestAsync<object>(endpoint, wrappedBody, cancellationToken).ConfigureAwait(false);
  }

  private static readonly ActivitySource s_activitySource = new("MTGOSDK.Core");

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
              if (_pendingRequests.TryRemove(frame.MessageId, out var tcs))
              {
                tcs.TrySetResult(frame.Body);
              }
              else
              {
                Log.Warning($"[TcpCommunicator] No pending request for response {frame.MessageId}");
              }
              break;

            case TcpMessageType.Callback:
              // Dispatch callback (fire-and-forget)
              try
              {
                _callbackHandler?.Invoke(frame.Endpoint, frame.Body);
              }
              catch (Exception ex)
              {
                Log.Error($"[TcpCommunicator] Callback handler error: {ex.Message}");
              }
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
      Log.Error($"[TcpCommunicator] Reader loop error: {ex.Message}");
    }
    finally
    {
      _isConnected = false;

      // Fail all pending requests
      foreach (var kvp in _pendingRequests)
      {
        kvp.Value.TrySetCanceled();
      }
      _pendingRequests.Clear();
    }
  }

  /// <summary>
  /// Disposes managed resources.
  /// </summary>
  public override void Dispose()
  {
    _cts.Cancel();
    _isConnected = false;

    // Wait for tasks to complete
    try { _readerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
    try { _writerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }

    // Dispose stream and client
    try { _stream?.Dispose(); } catch { }
    try { _client?.Dispose(); } catch { }

    // Dispose base class (completes pipelines)
    base.Dispose();

    _cts.Dispose();
  }
}
