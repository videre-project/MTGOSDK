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

using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;


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

    /// <summary>
    /// Decides when to answer: the connection index and endpoint.
    /// </summary>
    public Func<int, string, Task<bool>> Answer { get; set; } = (_, _) => Task.FromResult(false);

    /// <summary>
    /// The response body for an endpoint (null: a success without data).
    /// </summary>
    public Func<string, byte[]?> Body { get; set; } = _ => null;

    public Peer()
    {
      _listener.Start();
      _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Accepted { get { lock (_connections) return _connections.Count; } }

    /// <summary>
    /// The endpoints received so far on one connection.
    /// </summary>
    public string[] Requests(int connection)
    {
      lock (_connections)
        return connection < _requests.Count ? _requests[connection].ToArray() : [];
    }

    /// <summary>
    /// Closes one connection from the peer's side.
    /// </summary>
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
        byte[] body = Body(endpoint) ?? MessagePackSerializer.Serialize(DiverResponse<object>.Success(null!));
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

  /// <summary>
  /// The transport's current connection.
  /// </summary>
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

  /// <summary>
  /// A peer that answers everything: subscriptions and hooks with new tokens,
  /// and unregistrations with <paramref name="removed"/> as the Diver's answer.
  /// </summary>
  private static Peer SubscriptionPeer(Func<bool> removed)
  {
    int tokens = 0;
    return new Peer
    {
      Answer = (_, _) => Task.FromResult(true),
      Body = endpoint => endpoint switch
      {
        "/event_subscribe" or "/hook_method" => MessagePackSerializer.Serialize(
          DiverResponse<EventRegistrationResults>.Success(
            new EventRegistrationResults { Token = Interlocked.Increment(ref tokens) })),
        "/unregister_client" => MessagePackSerializer.Serialize(
          DiverResponse<UnregisterClientResponse>.Success(
            new UnregisterClientResponse { WasRemoved = removed() })),
        _ => null,
      },
    };
  }

  private static readonly DiverCommunicator.LocalEventCallback s_noEvent = (_, _) => { };

  /// <summary>
  /// A new hook callback that does nothing (a distinct instance on each call).
  /// </summary>
  private static LocalHookCallback NoHook()
  {
    var instance = new object();
    return (_, _, _) => GC.KeepAlive(instance);
  }

  [Test]
  public async Task Test_CleanupAfterAConfirmedUnregister_SendsNothing()
  {
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      var onEvent = s_noEvent;
      var onHook = NoHook();
      client.EventSubscribe(1, "Changed", onEvent);
      // Registering again keeps the same registration in the Diver.
      Assert.That(client.RegisterClient(), Is.True);
      client.HookMethod("T", "M", HarmonyPatchPosition.Prefix, onHook);
      Assert.That(client.UnregisterClient(), Is.True);

      // The connection drops; cleanup would go out on a new, unregistered one.
      peer.Close(0);
      await Until(() => !client.IsConnected, "the client sees the drop");
      Assert.That(() => client.EventUnsubscribe(onEvent), Throws.Nothing);
      Assert.That(() => client.UnhookMethod(onHook), Throws.Nothing);
      Assert.That(peer.Accepted, Is.EqualTo(1), "the Diver removed both with the registration");
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public void Test_ARetiredHookSharingATokenWithALiveOne_LeavesTheLiveOne()
  {
    using var peer = SubscriptionPeer(() => true);
    // One native hook: the Diver gives every share of it the same token.
    peer.Body = endpoint => endpoint switch
    {
      "/hook_method" => MessagePackSerializer.Serialize(
        DiverResponse<EventRegistrationResults>.Success(new EventRegistrationResults { Token = 7 })),
      "/unregister_client" => MessagePackSerializer.Serialize(
        DiverResponse<UnregisterClientResponse>.Success(new UnregisterClientResponse { WasRemoved = true })),
      _ => null,
    };
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      var retired = NoHook();
      client.HookMethod("T", "M", HarmonyPatchPosition.Prefix, retired);
      Assert.That(client.UnregisterClient(), Is.True);
      Assert.That(client.RegisterClient(), Is.True);
      var live = NoHook();
      client.HookMethod("T", "M", HarmonyPatchPosition.Prefix, live);

      client.UnhookMethod(retired);
      Assert.That(peer.Requests(0), Does.Not.Contain("/unhook_method"), "the retired share is gone already");
      Assert.That(client.GetCallbackMapDiagnostics().HookTokenEntries, Is.EqualTo(1),
        "the live hook still receives the token's callbacks");

      client.UnhookMethod(live);
      Assert.That(peer.Requests(0).Last(), Is.EqualTo("/unhook_method"));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_CleanupOverlappingAnUnregister_WaitsAndThenSendsNothing()
  {
    var unregistered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      var old = NoHook();
      client.HookMethod("T", "M", HarmonyPatchPosition.Prefix, old);

      // The unregister is under way (its answer held) when the old hook's
      // cleanup starts: the epoch has not ended yet.
      peer.Answer = (_, endpoint) => endpoint == "/unregister_client" ? unregistered.Task : Task.FromResult(true);
      var unregistering = Task.Run(() => client.UnregisterClient());
      await Until(() => peer.Requests(0).Contains("/unregister_client"), "the unregister is sent");
      var cleanup = Task.Run(() => client.UnhookMethod(old));
      await Task.Delay(500);
      Assert.That(peer.Requests(0), Does.Not.Contain("/unhook_method"),
        "no cleanup is sent while the registration changes");

      // Confirmed: the Diver removed the hook with the registration, so a
      // cleanup sent now (on whatever registration comes next) could only
      // remove another share.
      unregistered.SetResult(true);
      Assert.That(await unregistering, Is.True);
      await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(peer.Requests(0), Does.Not.Contain("/unhook_method"));
    }
    finally
    {
      unregistered.TrySetResult(false);
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_CleanupWaitingForTheLock_ReturnsOnceItsEpochEnds()
  {
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    using var release = new ManualResetEventSlim();
    using var held = new ManualResetEventSlim();
    Thread? holder = null;
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      var old = NoHook();
      client.HookMethod("T", "M", HarmonyPatchPosition.Prefix, old);

      // A newer request of this client holds the registration lock and does
      // not finish (as a subscription the Diver never answers would).
      object registrationLock = typeof(DiverCommunicator)
        .GetField("_registrationLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
      holder = new Thread(() =>
      {
        lock (registrationLock) { held.Set(); release.Wait(); }
      });
      holder.Start();
      held.Wait();

      // The old hook's cleanup starts while its epoch is current, so it waits.
      var cleanup = Task.Run(() => client.UnhookMethod(old));
      await Task.Delay(300);
      Assert.That(cleanup.IsCompleted, Is.False, "an active cleanup waits for the lock");

      // The epoch ends (as a confirmed unregister ends it): the cleanup returns
      // although the lock is still held, and sends nothing.
      SetField(client, "_epoch", 0L);
      await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(peer.Requests(0), Does.Not.Contain("/unhook_method"));
    }
    finally
    {
      release.Set();
      holder?.Join();
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_ASubscriptionOverlappingATimedOutUnregister_KeepsItsEpoch()
  {
    var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var peer = SubscriptionPeer(() => true);
    peer.Answer = (_, endpoint) => endpoint == "/event_subscribe" ? answer.Task : Task.FromResult(true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      var onEvent = s_noEvent;
      var subscribing = Task.Run(() => client.EventSubscribe(1, "Changed", onEvent));
      await Until(() => peer.Requests(0).Contains("/event_subscribe"), "the subscription is sent");

      // Disposal gives up while the subscription is unanswered.
      Assert.That(client.TryUnregisterClient(TimeSpan.FromMilliseconds(300)), Is.False);
      answer.SetResult(true);
      await subscribing;

      // The Diver made it under the registration; a confirmed unregister removes it.
      Assert.That(client.UnregisterClient(), Is.True);
      peer.Close(0);
      await Until(() => !client.IsConnected, "the client sees the drop");
      Assert.That(() => client.EventUnsubscribe(onEvent), Throws.Nothing);
      Assert.That(peer.Accepted, Is.EqualTo(1), "nothing is sent for it");
    }
    finally
    {
      answer.TrySetResult(false);
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_RetiredCleanup_DoesNotWaitForAnotherSubscription()
  {
    var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      DiverCommunicator.LocalEventCallback retired = (_, _) => { };
      client.EventSubscribe(1, "Changed", retired);
      Assert.That(client.UnregisterClient(), Is.True);
      Assert.That(client.RegisterClient(), Is.True);

      // A newer subscription waits for a Diver that does not answer.
      peer.Answer = (_, endpoint) => endpoint == "/event_subscribe" ? answer.Task : Task.FromResult(true);
      DiverCommunicator.LocalEventCallback waiting = (_, _) => { };
      var subscribing = Task.Run(() => client.EventSubscribe(1, "Changed", waiting));
      await Until(() => peer.Requests(0).Count(e => e == "/event_subscribe") == 2, "the newer subscription is sent");

      var cleanup = Task.Run(() => client.EventUnsubscribe(retired));
      Assert.That(await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(cleanup),
        "the retired cleanup does not wait for the newer subscription");
      await cleanup;
      Assert.That(peer.Requests(0), Does.Not.Contain("/event_unsubscribe"));

      answer.SetResult(true);
      await subscribing;
    }
    finally
    {
      answer.TrySetResult(false);
      client.Cancel();
    }
  }

  [Test]
  public void Test_CleanupAfterAnUnconfirmedUnregister_IsSent()
  {
    using var peer = SubscriptionPeer(() => false);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      var onEvent = s_noEvent;
      client.EventSubscribe(1, "Changed", onEvent);
      Assert.That(client.UnregisterClient(), Is.True);

      client.EventUnsubscribe(onEvent);
      Assert.That(peer.Requests(0).Last(), Is.EqualTo("/event_unsubscribe"));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public void Test_ASubscriptionMadeWhileUnregistered_StillSendsItsCleanup()
  {
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      // Not the registration's: the Diver does not remove it when the client
      // unregisters, so the client sends the request. (A Diver counts such a
      // subscription as an unregistered connection's, and this connection is
      // now its removed registration's: it takes nothing; that limitation is
      // older than this test.)
      var onHook = NoHook();
      client.HookMethod("T", "M", HarmonyPatchPosition.Prefix, onHook);
      Assert.That(client.RegisterClient(), Is.True);
      Assert.That(client.UnregisterClient(), Is.True);

      client.UnhookMethod(onHook);
      Assert.That(peer.Requests(0).Last(), Is.EqualTo("/unhook_method"));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public void Test_ANewRegistrationAfterAConfirmedUnregister_IsCleanedUp()
  {
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      Assert.That(client.UnregisterClient(), Is.True);
      // A new registration (and epoch) after the first was removed.
      Assert.That(client.RegisterClient(), Is.True);
      var onEvent = s_noEvent;
      client.EventSubscribe(1, "Changed", onEvent);

      client.EventUnsubscribe(onEvent);
      Assert.That(peer.Requests(0).Last(), Is.EqualTo("/event_unsubscribe"));
    }
    finally
    {
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
