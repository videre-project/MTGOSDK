/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MessagePack;
using System.Buffers;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// A wrapper for IPC requests that includes distributed tracing context.
/// </summary>
[MessagePackObject]
public sealed class TracedRequest
{
  /// <summary>
  /// The W3C TraceParent header value (e.g. 00-traceId-spanId-01).
  /// </summary>
  [Key(0)]
  public string TraceParent { get; set; }

  /// <summary>
  /// The W3C TraceState header value.
  /// </summary>
  [Key(1)]
  public string TraceState { get; set; }

  /// <summary>
  /// The actual request body (MessagePack serialized).
  /// </summary>
  [Key(2)]
  public byte[] Body { get; set; }

  /// <summary>
  /// Manually serializes the request to avoid resolver dependency issues in ILRepack environments.
  /// </summary>
  public static byte[] Serialize(TracedRequest request)
  {
    var bufferWriter = new SimpleBufferWriter();
    var writer = new MessagePackWriter(bufferWriter);

    // Write as array of 3 elements: [TraceParent, TraceState, Body]
    writer.WriteArrayHeader(3);
    writer.Write(request.TraceParent);
    writer.Write(request.TraceState);
    writer.Write(request.Body);

    writer.Flush();
    return bufferWriter.ToArray();
  }

  /// <summary>
  /// Manually deserializes the request to avoid resolver dependency issues in ILRepack environments.
  /// </summary>
  public static TracedRequest Deserialize(byte[] data)
  {
    var reader = new MessagePackReader(new System.ReadOnlyMemory<byte>(data));

    int count = reader.ReadArrayHeader();
    if (count != 3) 
      throw new MessagePackSerializationException($"Invalid TracedRequest array length: {count}");

    var req = new TracedRequest();
    req.TraceParent = reader.ReadString();
    req.TraceState = reader.ReadString();
    var sequence = reader.ReadBytes();
    req.Body = sequence?.ToArray();

    return req;
  }

  private class SimpleBufferWriter : IBufferWriter<byte>
  {
      private byte[] _buffer;
      private int _index;

      public SimpleBufferWriter(int initialCapacity = 256)
      {
          _buffer = new byte[initialCapacity];
      }

      public void Advance(int count)
      {
          _index += count;
      }

      public Memory<byte> GetMemory(int sizeHint = 0)
      {
          EnsureCapacity(sizeHint);
          return _buffer.AsMemory(_index);
      }

      public Span<byte> GetSpan(int sizeHint = 0)
      {
          EnsureCapacity(sizeHint);
          return _buffer.AsSpan(_index);
      }

      public byte[] ToArray()
      {
          return _buffer.AsSpan(0, _index).ToArray();
      }

      private void EnsureCapacity(int sizeHint)
      {
          if (sizeHint == 0) sizeHint = 1;
          if (_index + sizeHint > _buffer.Length)
          {
              int newSize = _buffer.Length * 2;
              while (_index + sizeHint > newSize)
              {
                  newSize *= 2;
              }
              Array.Resize(ref _buffer, newSize);
          }
      }
  }
}
