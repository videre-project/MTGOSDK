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
  internal sealed class Peer : IDisposable
  {
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _connections = new();
    private readonly List<List<string>> _requests = new();
    private readonly List<Task> _workers = new();
    private readonly Task _acceptTask;

    /// <summary>
    /// Decides when to answer: the connection index and endpoint.
    /// </summary>
    public Func<int, string, Task<bool>> Answer { get; set; } = (_, _) => Task.FromResult(false);

    /// <summary>
    /// The response body for an endpoint (null: a success without data).
    /// </summary>
    public Func<string, byte[]?> Body { get; set; } = _ => null;

    public Func<int, string, byte[], Task<byte[]?>>? Respond { get; set; }

    public Peer()
    {
      _listener.Start();
      _acceptTask = AcceptAsync();
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
          var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
          int index;
          lock (_connections)
          {
            if (_cts.IsCancellationRequested)
            {
              client.Dispose();
              return;
            }
            index = _connections.Count;
            _connections.Add(client);
            _requests.Add(new List<string>());
            _workers.Add(ServeAsync(index, client));
          }
        }
      }
      catch (Exception error) when (IsConnectionClosed(error)) { }
    }

    private async Task ServeAsync(int index, TcpClient client)
    {
      using var writeLock = new SemaphoreSlim(1, 1);
      var responses = new List<Task>();
      try
      {
        var stream = client.GetStream();
        var header = new byte[11];
        while (true)
        {
          await stream.ReadExactlyAsync(header).ConfigureAwait(false);
          int id = BinaryPrimitives.ReadInt32LittleEndian(header);
          int endpointLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5, 2));
          int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(7, 4));
          var endpointBytes = new byte[endpointLength];
          await stream.ReadExactlyAsync(endpointBytes).ConfigureAwait(false);
          var body = new byte[bodyLength];
          await stream.ReadExactlyAsync(body).ConfigureAwait(false);
          string endpoint = Encoding.UTF8.GetString(endpointBytes);
          lock (_connections) _requests[index].Add(endpoint);
          responses.Add(RespondAsync(index, endpoint, body, id, stream, writeLock));
        }
      }
      catch (Exception error) when (IsConnectionClosed(error)) { }
      finally
      {
        await Task.WhenAll(responses).ConfigureAwait(false);
      }
    }

    private async Task RespondAsync(int index, string endpoint, byte[] request, int id, NetworkStream stream, SemaphoreSlim writeLock)
    {
      try
      {
        if (!await Answer(index, endpoint).WaitAsync(_cts.Token).ConfigureAwait(false)) return;
        byte[] body;
        try
        {
          body = (Respond is null ? Body(endpoint) : await Respond(index, endpoint, request).ConfigureAwait(false))
            ?? MessagePackSerializer.Serialize(DiverResponse<object>.Success(null!));
        }
        catch (Exception error)
        {
          body = MessagePackSerializer.Serialize(DiverResponse<object>.FromError(error.Message));
        }
        var frame = new byte[11 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, id);
        frame[4] = (byte)TcpMessageType.Response;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7, 4), body.Length);
        body.CopyTo(frame, 11);
        await writeLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try { await stream.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false); }
        finally { writeLock.Release(); }
      }
      catch (Exception error) when (IsConnectionClosed(error)) { }
    }

    private static bool IsConnectionClosed(Exception error) =>
      error is IOException or SocketException or ObjectDisposedException or OperationCanceledException;

    public void Dispose()
    {
      using (_cts)
      {
        _cts.Cancel();
        _listener.Stop();
        lock (_connections)
          foreach (var connection in _connections) connection.Dispose();
        _acceptTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
      }
    }
  }

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
  public void Test_Disconnect_AfterAnUnansweredUnregister_SendsNothing()
  {
    using var peer = SubscriptionPeer(() => true);
    peer.Answer = (_, endpoint) => Task.FromResult(endpoint != "/unregister_client");
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);

      // RemoteClient.Dispose: a bounded unregister, then the handle's Disconnect.
      var watch = Stopwatch.StartNew();
      Assert.That(client.TryUnregisterClient(TimeSpan.FromMilliseconds(500)), Is.False);
      Assert.That(client.Disconnect(), Is.False, "nothing is left to unregister");
      Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "bounded unregister and disconnect finish without a response");
      Assert.That(peer.Requests(0), Is.EqualTo(new[] { "/register_client", "/unregister_client" }));
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_ConcurrentConnects_WaitForOneHandshake()
  {
    using var peer = new Peer { Answer = (_, _) => Task.FromResult(true) };
    using var tcp = new TcpCommunicator("127.0.0.1", peer.Port);
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int handshakes = 0;
    async Task Handshake(Func<string, byte[], Task> send)
    {
      Interlocked.Increment(ref handshakes);
      started.TrySetResult();
      await release.Task;
      await send("/register_client", []);
    }

    var first = tcp.ConnectAsync(Handshake);
    Task[] others = [];
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      others = Enumerable.Range(0, 7).Select(_ => tcp.ConnectAsync(Handshake)).ToArray();
      Assert.That(others.All(attempt => !attempt.IsCompleted), Is.True,
        "concurrent connection attempts wait for the pending handshake");

      release.SetResult();
      await Task.WhenAll(others.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(5));
      await tcp.SendRequestAsync("/ping", []).WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(handshakes, Is.EqualTo(1));
      Assert.That(peer.Accepted, Is.EqualTo(1));
      Assert.That(peer.Requests(0), Is.EqualTo(new[] { "/register_client", "/ping" }));
    }
    finally
    {
      release.TrySetResult();
      await Task.WhenAll(others.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(5));
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

      // Concurrent API requests must wait until the new connection is registered.
      var first = Task.Run(() => client.CheckAliveness());
      await Until(() => peer.Requests(1).Contains("/register_client"), "the registration is sent");
      var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var second = Task.Run(() =>
      {
        secondStarted.SetResult();
        return client.CheckAliveness();
      });
      await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
      await Task.Delay(500);
      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/register_client" }),
        "no request is sent on the new connection before its registration is confirmed");

      registered.SetResult(true);
      Assert.That(await first, Is.True);
      Assert.That(await second, Is.True);
      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/register_client", "/ping", "/ping" }));
    }
    finally
    {
      registered.TrySetResult(false);
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
      await Assert.ThatAsync(() => attempt.WaitAsync(TimeSpan.FromSeconds(5)),
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
        "/register_client" => MessagePackSerializer.Serialize(new DiverResponse<object>
          { PinOwner = "" }),
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
      var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var cleanup = Task.Run(() =>
      {
        cleanupStarted.SetResult();
        client.UnhookMethod(old);
      });
      await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
      await Task.Delay(500);
      Assert.That(cleanup.IsCompleted, Is.False, "cleanup waits for unregister to finish");
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
  public async Task Test_TimedOutUnregister_DoesNotDetachPendingSubscriptions()
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

      // The bounded unregister gives up while the subscription is unanswered.
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
  public void Test_CleanupAfterAnAlreadyRemovedRegistration_IsSkipped()
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
      Assert.That(peer.Requests(0), Does.Not.Contain("/event_unsubscribe"));
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
      // Unregistering cannot be assumed to remove a hook created before registration.
      // The client must still send its explicit unhook request.
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
  public void Test_SubscriptionAfterReregistering_StillSendsItsCleanup()
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

      await Assert.ThatAsync(() => attempt.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.InstanceOf<OperationCanceledException>(), "the cancelled attempt fails");
      await Assert.ThatAsync(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.InstanceOf<OperationCanceledException>(), "a request waiting for the cancelled handshake fails");
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
      peer.Close(0);
      await Assert.ThatAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(5)),
        Throws.InstanceOf<IOException>().Or.InstanceOf<OperationCanceledException>(),
        "a request of the closed connection fails");
      Assert.That(client.CheckAliveness(), Is.True);

      Assert.That(peer.Requests(1), Is.EqualTo(new[] { "/ping" }),
        "the failed diagnostics request is not replayed on the new connection");
    }
    finally
    {
      client.Cancel();
    }
  }

  [Test]
  public async Task Test_Disposal_ClosesTheConnectionAndPreventsReuse()
  {
    using var peer = new Peer { Answer = (_, _) => Task.FromResult(true) };
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    try
    {
      await client.ConnectAsync();
      Assert.That(client.CheckAliveness(), Is.True);
      client.Dispose();
      Assert.That(client.IsConnected, Is.False);
      Assert.DoesNotThrow(client.Cancel);
      Assert.DoesNotThrow(client.Dispose);
      await Assert.ThatAsync(() => client.ConnectAsync(), Throws.TypeOf<ObjectDisposedException>());
      Assert.That(peer.Accepted, Is.EqualTo(1), "disposal cannot open another connection");
    }
    finally
    {
      client.Dispose();
    }
  }

  [Test]
  public async Task Test_SlowSubscription_DoesNotLoseAnotherSubscriptionsCleanup()
  {
    using var peer = SubscriptionPeer(() => true);
    var client = new DiverCommunicator("127.0.0.1", peer.Port);
    var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    Task? subscribing = null;
    Task? cleanup = null;
    var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    try
    {
      Assert.That(client.RegisterClient(), Is.True);
      client.EventSubscribe(1, "Changed", s_noEvent);
      peer.Answer = (_, endpoint) => endpoint == "/event_subscribe" ? answer.Task : Task.FromResult(true);
      subscribing = Task.Run(() => client.EventSubscribe(1, "OtherChanged", (_, _) => { }));
      await Until(() => peer.Requests(0).Count(endpoint => endpoint == "/event_subscribe") == 2,
        "the second subscription is waiting for its response");
      cleanup = Task.Run(() =>
      {
        cleanupStarted.SetResult();
        client.EventUnsubscribe(s_noEvent);
      });
      await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
      // An unrelated subscription taking over a second must not make cleanup give up.
      await Task.Delay(1200);
      answer.SetResult(true);
      await subscribing.WaitAsync(TimeSpan.FromSeconds(5));
      await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(peer.Requests(0).Count(endpoint => endpoint == "/event_unsubscribe"), Is.EqualTo(1));
    }
    finally
    {
      answer.TrySetResult(true);
      try
      {
        if (subscribing is not null) await subscribing.WaitAsync(TimeSpan.FromSeconds(5));
        if (cleanup is not null) await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
      }
      finally
      {
        client.Dispose();
      }
    }
  }
}
