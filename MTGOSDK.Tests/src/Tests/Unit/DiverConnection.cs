/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MessagePack;

using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.UnitTests;

/// <summary>
/// A client's connection handling against a local peer that speaks the
/// Diver's frame protocol (no MTGO needed).
/// </summary>
public class DiverConnection
{
  /// <summary>
  /// Accepts connections, records each request's endpoint per connection, and
  /// answers a request once <see cref="Answer"/> allows it (never, by default).
  /// </summary>
  private sealed class Peer : IDisposable
  {
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _connections = new();
    private readonly List<List<string>> _requests = new();

    /// <summary>Decides when to answer: the connection index and endpoint.</summary>
    public Func<int, string, Task<bool>> Answer { get; set; } = (_, _) => Task.FromResult(false);

    public Peer()
    {
      _listener.Start();
      _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Accepted { get { lock (_connections) return _connections.Count; } }

    /// <summary>The endpoints received so far on one connection.</summary>
    public string[] Requests(int connection)
    {
      lock (_connections)
        return connection < _requests.Count ? _requests[connection].ToArray() : [];
    }

    /// <summary>Closes one connection from the peer's side.</summary>
    public void Close(int connection)
    {
      lock (_connections) _connections[connection].Close();
    }

    private async Task AcceptAsync()
    {
      try
      {
        while (!_cts.IsCancellationRequested)
        {
          var client = await _listener.AcceptTcpClientAsync();
          int index;
          lock (_connections)
          {
            index = _connections.Count;
            _connections.Add(client);
            _requests.Add(new List<string>());
          }
          _ = ServeAsync(index, client);
        }
      }
      catch (Exception) when (_cts.IsCancellationRequested) { }
      catch (ObjectDisposedException) { }
      catch (SocketException) { }
    }

    private async Task ServeAsync(int index, TcpClient client)
    {
      var stream = client.GetStream();
      var writeLock = new SemaphoreSlim(1, 1);
      try
      {
        var header = new byte[11];
        while (true)
        {
          await ReadExactlyAsync(stream, header);
          int id = BinaryPrimitives.ReadInt32LittleEndian(header);
          int endpointLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5, 2));
          int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(7, 4));
          var endpointBytes = new byte[endpointLength];
          await ReadExactlyAsync(stream, endpointBytes);
          await ReadExactlyAsync(stream, new byte[bodyLength]);
          string endpoint = Encoding.UTF8.GetString(endpointBytes);
          lock (_connections) _requests[index].Add(endpoint);
          _ = RespondAsync(index, endpoint, id, stream, writeLock);
        }
      }
      catch (Exception) { /* closed */ }
    }

    private async Task RespondAsync(int index, string endpoint, int id, NetworkStream stream, SemaphoreSlim writeLock)
    {
      try
      {
        if (!await Answer(index, endpoint)) return;
        byte[] body = MessagePackSerializer.Serialize(DiverResponse<object>.Success(null!));
        var frame = new byte[11 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, id);
        frame[4] = (byte)TcpMessageType.Response;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7, 4), body.Length);
        body.CopyTo(frame, 11);
        await writeLock.WaitAsync();
        try { await stream.WriteAsync(frame, 0, frame.Length); }
        finally { writeLock.Release(); }
      }
      catch (Exception) { /* closed */ }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer)
    {
      int read = 0;
      while (read < buffer.Length)
      {
        int n = await stream.ReadAsync(buffer, read, buffer.Length - read);
        if (n == 0) throw new EndOfStreamException();
        read += n;
      }
    }

    public void Dispose()
    {
      _cts.Cancel();
      _listener.Stop();
      lock (_connections)
        foreach (var connection in _connections) connection.Dispose();
    }
  }

  private static void SetField(object target, string name, object? value) =>
    target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

  private static TcpCommunicator Transport(DiverCommunicator client) =>
    (TcpCommunicator)typeof(DiverCommunicator)
      .GetField("_tcp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;

  /// <summary>The transport's current connection.</summary>
  private static object Session(DiverCommunicator client) =>
    typeof(TcpCommunicator)
      .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Transport(client))!;

  private static async Task Until(Func<bool> condition, string what)
  {
    var watch = Stopwatch.StartNew();
    while (!condition())
    {
      if (watch.Elapsed > TimeSpan.FromSeconds(10)) Assert.Fail($"Timed out waiting until {what}.");
      await Task.Delay(20);
    }
  }

  [Test]
  public async Task Test_Disconnect_AfterAnUnansweredUnregister_SendsNothing()
  {
    using var peer = new Peer();
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      await client.ConnectAsync();
      // As a registered client (this peer never confirms anything).
      SetField(client, "_registered", true);
      SetField(client, "_process_id", Environment.ProcessId);

      // RemoteClient.Dispose: a bounded unregister, then the handle's Disconnect.
      var watch = Stopwatch.StartNew();
      Assert.That(client.TryUnregisterClient(TimeSpan.FromMilliseconds(500)), Is.False);
      Assert.That(client.Disconnect(), Is.False, "nothing is left to unregister");
      Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "disposal does not wait for the peer");
      Assert.That(peer.Requests(0), Is.EqualTo(new[] { "/unregister_client" }));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_ConcurrentConnects_OpenOneConnection()
  {
    using var peer = new Peer();
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => client.ConnectAsync())));
      await Task.Delay(500);
      Assert.That(client.IsConnected, Is.True);
      Assert.That(peer.Accepted, Is.EqualTo(1));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_ReconnectedConnection_IsRegisteredBeforeAnyOtherRequest()
  {
    using var peer = new Peer();
    var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    // The second connection's registration is answered only when released.
    peer.Answer = (connection, endpoint) =>
      connection == 1 && endpoint == "/register_client" ? registered.Task : Task.FromResult(true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);

      peer.Close(0);
      await Until(() => !client.IsConnected, "the client sees the drop");

      // One request reconnects and waits for the registration. Another, sent
      // straight to the transport as a request that had already passed the
      // connection check would be, arrives while the registration is unanswered.
      var first = Task.Run(() => client.CheckAliveness());
      await Until(() => peer.Requests(1).Contains("/register_client"), "the registration is sent");
      var second = Transport(client).SendRequestAsync("/ping", []);
      await Task.Delay(500);
      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/register_client" }),
        "no request is sent on the new connection before its registration is confirmed");

      registered.SetResult(true);
      Assert.That(await first, Is.True);
      await second;
      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/register_client", "/ping", "/ping" }));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_CancelledReconnect_EndsAndAllowsTheNext()
  {
    using var peer = new Peer();
    var answerRegistration = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    // The second connection's registration is never answered.
    peer.Answer = (connection, endpoint) =>
      connection == 1 && endpoint == "/register_client" ? answerRegistration.Task : Task.FromResult(true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      peer.Close(0);
      await Until(() => !client.IsConnected, "the client sees the drop");

      using var timeout = new CancellationTokenSource();
      var attempt = client.ConnectAsync(timeout.Token);
      await Until(() => peer.Requests(1).Contains("/register_client"), "the registration is sent");
      timeout.Cancel();
      Assert.That(async () => await attempt.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.InstanceOf<OperationCanceledException>(), "the cancelled attempt ends");
      Assert.That(client.IsConnected, Is.False, "its connection was closed");

      // The next attempt connects and registers again.
      Assert.That(client.CheckAliveness(), Is.True);
      Assert.That(peer.Requests(2), Is.EqualTo(new[] { "/register_client", "/ping" }));
    }
    finally
    {
      answerRegistration.TrySetResult(false);
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_HandshakeCompletingAfterCancellation_ClosesTheConnection()
  {
    using var peer = new Peer();
    peer.Answer = (_, _) => Task.FromResult(true);
    var tcp = new TcpCommunicator("127.0.0.1", peer.Port);
    try
    {
      var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using var cancel = new CancellationTokenSource();
      // A handshake that does not observe the token: it completes normally.
      var attempt = tcp.ConnectAsync(async _ =>
      {
        started.SetResult();
        await release.Task;
      }, cancel.Token);
      await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

      // An ordinary request waits for the handshake.
      var waiting = tcp.SendRequestAsync("/ping", []);
      cancel.Cancel();
      release.SetResult();

      Assert.That(async () => await attempt.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.InstanceOf<OperationCanceledException>(), "the cancelled attempt fails");
      Assert.That(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.Exception, "a request waiting for the handshake fails");
      Assert.That(tcp.IsConnected, Is.False, "its connection was closed");
      Assert.That(peer.Requests(0), Is.Empty, "no ordinary request reached the peer");

      // The next attempt connects.
      await tcp.ConnectAsync();
      await tcp.SendRequestAsync("/ping", []);
      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/ping" }));
    }
    finally
    {
      tcp.Dispose();
    }
  }

  [Test]
  public async Task Test_CancellingAfterAConnect_KeepsTheConnection()
  {
    using var peer = new Peer();
    peer.Answer = (_, _) => Task.FromResult(true);
    var tcp = new TcpCommunicator("127.0.0.1", peer.Port);
    try
    {
      using var cancel = new CancellationTokenSource();
      await tcp.ConnectAsync(send => send("/register_client", []), cancel.Token);
      cancel.Cancel();

      await tcp.SendRequestAsync("/ping", []);
      Assert.That(tcp.IsConnected, Is.True, "the token does not outlive the attempt");
      Assert.That(peer.Requests(0), Is.EqualTo(new[] { "/register_client", "/ping" }));
    }
    finally
    {
      tcp.Dispose();
    }
  }

  [Test]
  public async Task Test_RequestOfAClosedConnection_IsNeverSentOnTheNext()
  {
    using var peer = new Peer();
    // Only pings are answered; the diagnostics request waits forever.
    peer.Answer = (_, endpoint) => Task.FromResult(endpoint == "/ping");
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      var pending = Task.Run(() => client.GetDiverDiagnostics());
      await Until(() => peer.Requests(0).Contains("/diagnostics"), "the request is sent");
      object closed = Session(client);

      peer.Close(0);
      Assert.That(async () => await pending, Throws.Exception, "a request of the closed connection fails");
      Assert.That(client.CheckAliveness(), Is.True);

      // A send bound to the closed connection that resumes after the
      // reconnect (for example one paused before it was queued) fails there.
      var late = (Task)closed.GetType().GetMethod("SendAsync")!
        .Invoke(closed, [999, "/get_field", Array.Empty<byte>(), false, CancellationToken.None])!;
      Assert.That(async () => await late, Throws.InvalidOperationException);

      Assert.That(client.CheckAliveness(), Is.True);
      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/ping", "/ping" }),
        "nothing of the closed connection is sent on the new one");
    }
    finally
    {
      client.Cancel();
    }
  }
}
