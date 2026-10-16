/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using MessagePack;

using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.UnitTests;

/// <summary>
/// Executes serializer requests against managed objects over the Diver's TCP protocol.
/// </summary>
internal sealed class SerializationPeer : IDisposable
{
  internal sealed class PendingCapture
  {
    private readonly TaskCompletionSource<bool> _release =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Released => _release.Task;
    public void Complete() => _release.TrySetResult(true);
  }

  private sealed record HeldRead(string Name, TaskCompletionSource<PendingCapture> Captured);

  private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
  private readonly CancellationTokenSource _stop = new();
  private readonly ConcurrentBag<Connection> _connections = new();
  private readonly ConcurrentDictionary<ulong, object> _objects = new();
  private readonly BatchExecutor _executor;
  private readonly Task _accept;

  private TaskCompletionSource<PendingCapture>? _heldCapture;
  private HeldRead? _heldRead;
  private long _nextToken;
  private int _batchRequests;

  public RemoteHandle Handle { get; }
  public int BatchRequests => Volatile.Read(ref _batchRequests);

  public SerializationPeer()
  {
    _executor = new BatchExecutor(type => ResolveType(type.FullName), input =>
      input.IsRemoteAddress ? _objects[input.RemoteAddress]
        : PrimitivesEncoder.Decode(input.EncodedObject, input.Type));
    _listener.Start();
    _accept = AcceptAsync();
    try
    {
      Handle = RemoteHandle.Connect(Process.GetCurrentProcess(),
        (ushort)((IPEndPoint)_listener.LocalEndpoint).Port);
    }
    catch
    {
      Stop();
      throw;
    }
  }

  public DynamicRemoteObject Add(object value) =>
    (DynamicRemoteObject)Handle.GetRemoteObjectFromField(Pin(value), value.GetType().FullName!).Dynamify();

  public Task<PendingCapture> HoldNextCapture()
  {
    var capture = new TaskCompletionSource<PendingCapture>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    if (Interlocked.CompareExchange(ref _heldCapture, capture, null) != null)
      throw new InvalidOperationException("A capture is already waiting to be held.");
    return capture.Task;
  }

  public Task<PendingCapture> HoldNextRead(string name)
  {
    var capture = new TaskCompletionSource<PendingCapture>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    if (Interlocked.CompareExchange(ref _heldRead, new HeldRead(name, capture), null) != null)
      throw new InvalidOperationException("A read is already waiting to be held.");
    return capture.Task;
  }

  private async Task WaitForRelease(TaskCompletionSource<PendingCapture>? capture)
  {
    if (capture == null) return;
    var pending = new PendingCapture();
    capture.SetResult(pending);
    await pending.Released.WaitAsync(_stop.Token);
  }

  private ulong Pin(object value)
  {
    var token = (ulong)Interlocked.Increment(ref _nextToken);
    _objects[token] = value;
    return token;
  }

  private static Type ResolveType(string name) =>
    AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name))
      .First(type => type != null)!;

  private static byte[] Success<T>(T value) =>
    MessagePackSerializer.Serialize(DiverResponse<T>.Success(value));

  private async Task<byte[]> RespondAsync(string endpoint, byte[] body)
  {
    switch (endpoint)
    {
      case "/ping":
      case "/register_client":
      case "/unregister_client":
      case "/unpin":
        return Success<object?>(null);
      case "/domains":
        return Success(new DomainDump("serialization", new List<string>()));
      case "/type":
        var type = MessagePackSerializer.Deserialize<TypeDumpRequest>(body);
        return Success(TypeDump.ParseType(ResolveType(type.TypeFullName)));
      case "/invoke":
        var invocation = MessagePackSerializer.Deserialize<InvocationRequest>(body);
        if (invocation.Parameters.Count != 0 || !invocation.MethodName.StartsWith("get_", StringComparison.Ordinal))
          throw new NotSupportedException(invocation.MethodName);
        var name = invocation.MethodName.Substring(4);
        var value = _executor.Read(_objects[invocation.ObjAddress], name);
        var heldRead = Volatile.Read(ref _heldRead);
        if (heldRead?.Name == name && Interlocked.CompareExchange(ref _heldRead, null, heldRead) == heldRead)
          await WaitForRelease(heldRead.Captured);
        return Success(new InvocationResults
        {
          ReturnedObjectOrAddress = value == null ? ObjectOrRemoteAddress.Null
            : ObjectOrRemoteAddress.FromObj(value)
        });
      case "/batch_execute":
        var request = MessagePackSerializer.Deserialize<RemoteBatchRequest>(body);
        var result = _executor.Execute(request);
        Interlocked.Increment(ref _batchRequests);
        var held = Interlocked.Exchange(ref _heldCapture, null);
        await WaitForRelease(held);
        return Success(result);
      case "/batch_collection":
        var collection = MessagePackSerializer.Deserialize<BatchCollectionRequest>(body);
        var items = ((System.Collections.IEnumerable)_objects[collection.CollectionAddress])
          .Cast<object>().Take(collection.MaxItems > 0 ? collection.MaxItems : int.MaxValue).ToArray();
        var paths = collection.PathsDelimited.Split('|');
        var columns = paths.Select(path => items.Select(item => _executor.Read(item, path)).ToArray()).ToArray();
        return Success(new BatchCollectionResponse
        {
          Schema = paths,
          SchemaTypes = columns.Select(column =>
            column.FirstOrDefault(value => value != null)?.GetType().FullName ?? "null").ToArray(),
          Columns = columns.Select(column => column.Select(value =>
            value == null ? null : PrimitivesEncoder.Encode(value)).ToArray()).ToArray(),
          Count = items.Length,
          ItemTokens = items.Select(Pin).ToList(),
          ItemTypeName = items.FirstOrDefault()?.GetType().FullName!
        });
      default:
        throw new NotSupportedException(endpoint);
    }
  }

  private async Task AcceptAsync()
  {
    try
    {
      while (!_stop.IsCancellationRequested)
      {
        var client = await _listener.AcceptTcpClientAsync(_stop.Token);
        _connections.Add(new Connection(client, this));
      }
    }
    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
  }

  private sealed class Connection : TcpPipelineBase
  {
    private readonly TcpClient _client;
    private readonly SerializationPeer _peer;
    private readonly Task _reader;
    private readonly Task _writer;

    public Connection(TcpClient client, SerializationPeer peer)
    {
      _client = client;
      _peer = peer;
      InitializePipelines(client.GetStream());
      _isConnected = true;
      _writer = WriterLoopAsync(peer._stop.Token);
      _reader = ReadAsync();
    }

    private async Task ReadAsync()
    {
      try
      {
        while (!_peer._stop.IsCancellationRequested)
        {
          var read = await _pipeReader.ReadAsync(_peer._stop.Token);
          var buffer = read.Buffer;
          while (TryParseFrame(ref buffer, out var frame))
            _ = ReplyAsync(frame.MessageId, frame.Endpoint, frame.Body);
          _pipeReader.AdvanceTo(buffer.Start, buffer.End);
          if (read.IsCompleted) break;
        }
      }
      catch (Exception) when (_peer._stop.IsCancellationRequested) { }
    }

    private async Task ReplyAsync(int id, string endpoint, byte[] body)
    {
      try
      {
        byte[] response;
        try
        {
          response = await _peer.RespondAsync(endpoint, body);
        }
        catch (Exception error) when (!_peer._stop.IsCancellationRequested)
        {
          response = MessagePackSerializer.Serialize(
            DiverResponse<object>.FromError(error.Message, error.ToString()));
        }
        await EnqueueWriteAsync(id, TcpMessageType.Response, endpoint, response, _peer._stop.Token);
      }
      catch (Exception) when (_peer._stop.IsCancellationRequested) { }
    }

    public override void Dispose()
    {
      _client.Dispose();
      base.Dispose();
      Task.WhenAll(_reader, _writer).GetAwaiter().GetResult();
    }
  }

  public void Dispose()
  {
    var communicator = Handle.Communicator;
    try
    {
      Handle.Dispose();
    }
    finally
    {
      communicator.Dispose();
      Stop();
    }
  }

  private void Stop()
  {
    _stop.Cancel();
    _listener.Stop();
    _accept.GetAwaiter().GetResult();
    foreach (var connection in _connections) connection.Dispose();
    _stop.Dispose();
  }
}
