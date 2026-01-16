/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Message type for TCP frame routing.
/// </summary>
public enum TcpMessageType : byte
{
  /// <summary>SDK → Diver request</summary>
  Request = 0x01,
  /// <summary>Diver → SDK response (correlated by message ID)</summary>
  Response = 0x02,
  /// <summary>Diver → SDK callback (fire-and-forget)</summary>
  Callback = 0x03
}

/// <summary>
/// Abstract base class for TCP communication using System.IO.Pipelines.
/// Provides zero-copy frame parsing and writing, shared by both client and server.
/// </summary>
public abstract class TcpPipelineBase : IDisposable
{
  // Frame format: [id:4][type:1][endpoint_len:2][body_len:4] = 11 bytes
  protected const int HeaderSize = 11;

  // Pipelines for zero-copy I/O
  protected PipeReader _pipeReader;
  protected PipeWriter _pipeWriter;

  // Write channel for lock-free async writes
  protected Channel<WriteRequest> _writeChannel = CreateWriteChannel();

  /// <summary>
  /// Creates a new bounded write channel.
  /// </summary>
  private static Channel<WriteRequest> CreateWriteChannel() =>
    Channel.CreateBounded<WriteRequest>(new BoundedChannelOptions(500)
    {
      SingleReader = true,
      SingleWriter = false,
      FullMode = BoundedChannelFullMode.Wait
    });

  /// <summary>
  /// Reinitializes the write channel for a new connection.
  /// Call this after completing the previous channel.
  /// </summary>
  protected void ReinitializeWriteChannel()
  {
    _writeChannel = CreateWriteChannel();
  }

  // Connection state
  protected volatile bool _isConnected = false;

  // Hash-keyed endpoint cache to avoid string allocations
  private static readonly Dictionary<int, string> s_endpointCache = new()
  {
    [ComputeEndpointHash("/ping")] = "/ping",
    [ComputeEndpointHash("/register_client")] = "/register_client",
    [ComputeEndpointHash("/unregister_client")] = "/unregister_client",
    [ComputeEndpointHash("/invoke")] = "/invoke",
    [ComputeEndpointHash("/get_field")] = "/get_field",
    [ComputeEndpointHash("/set_field")] = "/set_field",
    [ComputeEndpointHash("/heap")] = "/heap",
    [ComputeEndpointHash("/type")] = "/type",
    [ComputeEndpointHash("/types")] = "/types",
    [ComputeEndpointHash("/object")] = "/object",
    [ComputeEndpointHash("/domains")] = "/domains",
    [ComputeEndpointHash("/create_object")] = "/create_object",
    [ComputeEndpointHash("/unpin")] = "/unpin",
    [ComputeEndpointHash("/object_type")] = "/object_type",
    [ComputeEndpointHash("/get_item")] = "/get_item",
    [ComputeEndpointHash("/event_subscribe")] = "/event_subscribe",
    [ComputeEndpointHash("/event_unsubscribe")] = "/event_unsubscribe",
    [ComputeEndpointHash("/hook_method")] = "/hook_method",
    [ComputeEndpointHash("/unhook_method")] = "/unhook_method",
    [ComputeEndpointHash("/invoke_callback")] = "/invoke_callback",
  };

  /// <summary>
  /// Write request record for channel-based writes.
  /// </summary>
  protected readonly record struct WriteRequest(
    int MessageId,
    TcpMessageType Type,
    string Endpoint,
    byte[] Body);

  /// <summary>
  /// Parsed frame structure.
  /// </summary>
  protected readonly struct ParsedFrame
  {
    public readonly int MessageId;
    public readonly TcpMessageType Type;
    public readonly string Endpoint;
    public readonly byte[] Body;

    public ParsedFrame(int messageId, TcpMessageType type, string endpoint, byte[] body)
    {
      MessageId = messageId;
      Type = type;
      Endpoint = endpoint;
      Body = body;
    }
  }

  /// <summary>
  /// Whether the connection is active.
  /// </summary>
  public bool IsConnected => _isConnected;

  #region Endpoint Caching

  /// <summary>
  /// Computes FNV-1a hash for an endpoint string (for cache lookup).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected static int ComputeEndpointHash(string endpoint)
  {
    unchecked
    {
      int hash = (int)2166136261;
      foreach (char c in endpoint)
        hash = (hash ^ c) * 16777619;
      return hash;
    }
  }

  /// <summary>
  /// Computes FNV-1a hash from raw bytes (for zero-copy parsing).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected static int ComputeEndpointHash(ReadOnlySpan<byte> endpointBytes)
  {
    unchecked
    {
      int hash = (int)2166136261;
      foreach (byte b in endpointBytes)
        hash = (hash ^ b) * 16777619;
      return hash;
    }
  }

  /// <summary>
  /// Parses endpoint from bytes, returning cached string if possible (zero-alloc on hot paths).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected static string ParseEndpoint(ReadOnlySpan<byte> endpointBytes)
  {
    int hash = ComputeEndpointHash(endpointBytes);
    if (s_endpointCache.TryGetValue(hash, out var cached))
      return cached;

    // Fallback: allocate new string for unknown endpoints
#if NET5_0_OR_GREATER
    return Encoding.UTF8.GetString(endpointBytes);
#else
    return Encoding.UTF8.GetString(endpointBytes.ToArray());
#endif
  }

  #endregion

  #region Frame Parsing

  /// <summary>
  /// Tries to parse a complete frame from the buffer.
  /// Returns false if more data is needed.
  /// </summary>
  protected bool TryParseFrame(ref ReadOnlySequence<byte> buffer, out ParsedFrame frame)
  {
    frame = default;

    // Need at least header
    if (buffer.Length < HeaderSize)
      return false;

    // Read header into stack-allocated span for speed
    Span<byte> header = stackalloc byte[HeaderSize];
    buffer.Slice(0, HeaderSize).CopyTo(header);

    int messageId = BinaryPrimitives.ReadInt32LittleEndian(header);
    var messageType = (TcpMessageType)header[4];
    int endpointLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(5, 2));
    int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(7, 4));

    int totalFrameLength = HeaderSize + endpointLength + bodyLength;

    // Need complete frame
    if (buffer.Length < totalFrameLength)
      return false;

    // Extract endpoint (use cache for zero-alloc on hot paths)
    ReadOnlySequence<byte> endpointSeq = buffer.Slice(HeaderSize, endpointLength);
    string endpoint;
    if (endpointSeq.IsSingleSegment)
    {
      endpoint = ParseEndpoint(endpointSeq.First.Span);
    }
    else
    {
      // Multi-segment: copy to contiguous buffer
      Span<byte> endpointSpan = endpointLength <= 256
        ? stackalloc byte[endpointLength]
        : new byte[endpointLength];
      endpointSeq.CopyTo(endpointSpan);
      endpoint = ParseEndpoint(endpointSpan);
    }

    // Extract body (must copy since buffer will be released)
    byte[] body = new byte[bodyLength];
    buffer.Slice(HeaderSize + endpointLength, bodyLength).CopyTo(body);

    // Advance buffer past this frame
    buffer = buffer.Slice(totalFrameLength);

    frame = new ParsedFrame(messageId, messageType, endpoint, body);
    return true;
  }

  #endregion

  #region Frame Writing

  /// <summary>
  /// Writes a framed message to the PipeWriter buffer WITHOUT flushing.
  /// Used for opportunistic batching - call FlushAsync separately.
  /// </summary>
  protected void WriteFrameToBuffer(
    int messageId,
    TcpMessageType messageType,
    string endpoint,
    byte[] body)
  {
    // Get endpoint bytes (unavoidable allocation for .NET Framework compatibility)
    byte[] endpointBytes = Encoding.UTF8.GetBytes(endpoint);
    int frameSize = HeaderSize + endpointBytes.Length + body.Length;

    // Get buffer from PipeWriter (zero-copy)
    Span<byte> buffer = _pipeWriter.GetSpan(frameSize);

    // Write header: [id:4][type:1][endpoint_len:2][body_len:4]
    BinaryPrimitives.WriteInt32LittleEndian(buffer, messageId);
    buffer[4] = (byte)messageType;
    BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(5, 2), (ushort)endpointBytes.Length);
    BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(7, 4), body.Length);

    // Write endpoint
    endpointBytes.CopyTo(buffer.Slice(HeaderSize));

    // Write body
    body.CopyTo(buffer.Slice(HeaderSize + endpointBytes.Length));

    // Advance PipeWriter by the written amount (no flush yet)
    _pipeWriter.Advance(frameSize);
  }

  /// <summary>
  /// Writes a framed message asynchronously using PipeWriter (zero-copy) and flushes.
  /// </summary>
  protected async Task WriteFrameAsync(
    int messageId,
    TcpMessageType messageType,
    string endpoint,
    byte[] body,
    CancellationToken cancellationToken)
  {
    WriteFrameToBuffer(messageId, messageType, endpoint, body);
    await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Enqueues a write request to the channel (non-blocking).
  /// </summary>
  protected bool TryEnqueueWrite(int messageId, TcpMessageType type, string endpoint, byte[] body)
  {
    return _writeChannel.Writer.TryWrite(new WriteRequest(messageId, type, endpoint, body));
  }

  /// <summary>
  /// Enqueues a write request to the channel (async, waits if full).
  /// </summary>
  protected ValueTask EnqueueWriteAsync(
    int messageId,
    TcpMessageType type,
    string endpoint,
    byte[] body,
    CancellationToken cancellationToken)
  {
    return _writeChannel.Writer.WriteAsync(
      new WriteRequest(messageId, type, endpoint, body),
      cancellationToken);
  }

  /// <summary>
  /// Writer loop that drains the write channel and sends frames.
  /// Uses opportunistic batching: drains all available items, then flushes once.
  /// This provides optimal latency (immediate flush when idle) and throughput (batched under load).
  /// </summary>
  protected async Task WriterLoopAsync(CancellationToken cancellationToken)
  {
    try
    {
      while (await _writeChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
      {
        // Opportunistic batching: drain all immediately available items
        int batchCount = 0;
        while (_writeChannel.Reader.TryRead(out var req))
        {
          WriteFrameToBuffer(req.MessageId, req.Type, req.Endpoint, req.Body);
          batchCount++;
        }

        // Flush once after draining all pending items
        // This is optimal: single item = immediate flush, many items = batched flush
        if (batchCount > 0)
        {
          await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
      }
    }
    catch (OperationCanceledException)
    {
      // Expected on shutdown
    }
    catch (Exception ex)
    {
      MTGOSDK.Core.Logging.Log.Error($"[TcpPipelineBase] Writer loop error: {ex.Message}");
    }
  }

  #endregion

  #region Lifecycle

  /// <summary>
  /// Initializes PipeReader and PipeWriter from a stream.
  /// </summary>
  protected void InitializePipelines(System.IO.Stream stream)
  {
    _pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
    _pipeWriter = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
  }

  /// <summary>
  /// Completes the pipelines gracefully.
  /// </summary>
  protected void CompletePipelines()
  {
    try { _pipeReader?.Complete(); } catch { }
    try { _pipeWriter?.Complete(); } catch { }
  }

  /// <summary>
  /// Disposes of managed resources.
  /// </summary>
  public virtual void Dispose()
  {
    _isConnected = false;
    _writeChannel.Writer.TryComplete();
    CompletePipelines();
  }

  #endregion
}
