/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using MessagePack;
using ScubaDiver;

using MTGOSDK.Core;
using MTGOSDK.Core.Memory.Snapshot;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;
using MTGOSDK.Core.Remoting.Reflection;
using MTGOSDK.Core.Remoting.Types;

namespace MTGOSDK.UnitTests;

/// <summary>
/// Exercises SDK holders against a protocol peer backed by the production pin table.
/// Requests can be held at receipt boundaries to control collection and registration.
/// </summary>
[NonParallelizable]
public class RemoteLifetimes
{
  public sealed class Item : INotifyPropertyChanged
  {
    private string _name = "retained";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name
    {
      get => _name;
      set
      {
        _name = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
      }
    }
    public string Label = "retained";
    public Item() { }
    public Item(Item other) => Name = other.Name;
    public string Echo(Item other) => other.Name;
  }

  public sealed class RetryItem { }

  [MessagePackObject]
  public sealed class ExpectedUnpin
  {
    [Key(0)] public ulong Address { get; set; }
    [Key(1)] public string? PinOwner { get; set; }
  }

  private sealed class PinPeer : IDisposable
  {
    private readonly DiverConnection.Peer _peer = new();
    private readonly SnapshotRuntime _runtime = new();
    private readonly Dictionary<string, int> _clients = new();
    private readonly Dictionary<int, string> _connections = new();
    private readonly object _lock = new();
    private int _nextClient;
    public RemoteHandle Handle { get; }
    public PropertyChangeRouter Router { get; } = new();
    public DiverCommunicator Client => Handle.Communicator;
    public int Pins { get { lock (_lock) return _clients.Values.Sum(_runtime.GetClientReferenceCount); } }
    public int ObjectReads { get; private set; }
    public string? LastMemberValue { get; private set; }
    public Func<bool> RejectTypeRead { get; set; } = () => false;
    public bool TakeObjectReference { get; set; } = true;
    public bool RejectRepeatedObjectReads { get; set; }
    public bool FailSubscribe { get; set; }
    public bool LoseUnregisterResponse { get; set; }
    public bool RemoveBeforeDrop { get; set; } = true;
    public string? LastRegistration { get; private set; }
    public TaskCompletionSource<bool>? BatchEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueBatch { get; set; }
    public bool DelayNextUnregister { get; set; }
    public TaskCompletionSource<bool>? UnregisterEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueUnregister { get; set; }
    public TaskCompletionSource<bool>? DelayedUnregisterCompleted { get; set; }
    public TaskCompletionSource<bool>? ObjectEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueObject { get; set; }

    public bool Legacy { get; }
    public TaskCompletionSource<bool>? InvocationEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueInvocation { get; set; }
    public TaskCompletionSource<bool>? UnpinEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueUnpin { get; set; }
    public TaskCompletionSource<bool>? SubscribeEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueSubscribe { get; set; }
    public TaskCompletionSource<bool>? UnsubscribeEntered { get; set; }
    public TaskCompletionSource<bool>? ContinueUnsubscribe { get; set; }
    private RemoteBatchRequest? _capture;

    public PinPeer(bool legacy = false)
    {
      Legacy = legacy;
      if (legacy)
      {
        _clients.Add("other-client", ++_nextClient);
        _runtime.AddClient(_nextClient);
      }
      _peer.Answer = (_, _) => Task.FromResult(true);
      _peer.Respond = Respond;
      try
      {
        Handle = RemoteHandle.Connect(Process.GetCurrentProcess(), (ushort)_peer.Port);
      }
      catch
      {
        using var runtime = _runtime;
        using var peer = _peer;
        using var router = Router;
        throw;
      }
    }

    public void DropConnection() => _peer.Close(_peer.Accepted - 1);

    public bool IsPinned(ulong token) => _runtime.TryGetPinnedObject(token, out _);

    public ulong Pin(object value)
    {
      lock (_lock) return _runtime.PinObject(value, Legacy ? null : _clients[LastRegistration!]);
    }

    public RemoteObject Add(Item item) =>
      Handle.GetRemoteObjectFromField(Pin(item), typeof(Item).FullName!);

    public string[] Requests => Enumerable.Range(0, _peer.Accepted)
      .SelectMany(_peer.Requests).ToArray();

    private byte[] Success<T>(T data, string? owner)
    {
      var buffer = new ArrayBufferWriter<byte>();
      var writer = new MessagePackWriter(buffer);
      writer.WriteArrayHeader(Legacy ? 4 : 5);
      writer.Write(false);
      writer.WriteNil();
      writer.WriteNil();
      MessagePackSerializer.Serialize(ref writer, data);
      if (!Legacy) writer.Write(owner);
      writer.Flush();
      return buffer.WrittenSpan.ToArray();
    }

    private async Task<byte[]?> Respond(int connection, string endpoint, byte[] body)
    {
      // The owner is captured at request entry, before any controlled wait.
      string owner;
      lock (_lock) owner = _connections.GetValueOrDefault(connection, "");
      if ((endpoint is "/invoke" or "/create_object" or "/get_field" or "/set_field") &&
          ContinueInvocation is not null)
      {
        InvocationEntered!.TrySetResult(true);
        await ContinueInvocation.Task;
      }
      if (endpoint == "/unpin" && ContinueUnpin is not null)
      {
        UnpinEntered!.TrySetResult(true);
        await ContinueUnpin.Task;
      }
      if (endpoint == "/event_subscribe" && ContinueSubscribe is not null)
      {
        SubscribeEntered!.TrySetResult(true);
        await ContinueSubscribe.Task;
      }
      if (endpoint == "/event_unsubscribe" && ContinueUnsubscribe is not null)
      {
        UnsubscribeEntered!.TrySetResult(true);
        await ContinueUnsubscribe.Task;
      }
      bool delayedUnregister = false;
      if (endpoint == "/unregister_client")
      {
        lock (_lock)
        {
          delayedUnregister = DelayNextUnregister;
          DelayNextUnregister = false;
        }
        if (delayedUnregister)
        {
          UnregisterEntered!.TrySetResult(true);
          await ContinueUnregister!.Task;
        }
      }
      if (endpoint == "/batch_execute" && ContinueBatch is not null)
      {
        BatchEntered!.TrySetResult(true);
        await ContinueBatch.Task;
      }
      if (endpoint == "/object" && ContinueObject is not null)
      {
        byte[] response;
        lock (_lock) response = DumpObject(body, owner);
        ObjectEntered!.TrySetResult(true);
        await ContinueObject.Task;
        return response;
      }
      lock (_lock)
      {
        switch (endpoint)
        {
          case "/ping": return Success("pong", owner);
          case "/register_client":
            var register = MessagePackSerializer.Deserialize<RegisterClientRequest>(body);
            string key = $"{register.ProcessId}/{register.ClientId}";
            if (!_clients.TryGetValue(key, out int id))
            {
              _clients.Add(key, id = ++_nextClient);
              _runtime.AddClient(id);
            }
            _connections[connection] = LastRegistration = key;
            return Success<object?>(null, owner);
          case "/unregister_client":
            var unregister = MessagePackSerializer.Deserialize<UnregisterClientRequest>(body);
            string removedKey = $"{unregister.ProcessId}/{unregister.ClientId}";
            if (LoseUnregisterResponse && !RemoveBeforeDrop)
            {
              LoseUnregisterResponse = false;
              _peer.Close(connection);
              return null;
            }
            bool removed = _clients.Remove(removedKey, out int removedId);
            if (removed) _runtime.RemoveClient(removedId);
            if (delayedUnregister) DelayedUnregisterCompleted!.TrySetResult(true);
            if (LoseUnregisterResponse)
            {
              LoseUnregisterResponse = false;
              _peer.Close(connection);
              return null;
            }
            return Success(new UnregisterClientResponse
            { WasRemoved = removed, OtherClientsAmount = _clients.Count }, owner);
          case "/domains": return Success(new DomainDump("pins", []), owner);
          case "/type":
            if (RejectTypeRead())
              return MessagePackSerializer.Serialize(DiverResponse<object>.FromError("metadata unavailable"));
            var type = MessagePackSerializer.Deserialize<TypeDumpRequest>(body);
            var actual = _runtime.ResolveType(type.TypeFullName, type.Assembly);
            return Success(TypeDump.ParseType(actual), owner);
          case "/object": return DumpObject(body, owner);
          case "/invoke":
            var invoke = MessagePackSerializer.Deserialize<InvocationRequest>(body);
            var target = (Item)Resolve(invoke.ObjAddress);
            var method = target.GetType().GetMethod(invoke.MethodName)!;
            var result = method.Invoke(target, invoke.Parameters.Select(_runtime.ParseParameterObject).ToArray());
            LastMemberValue = target.Name;
            return Success(new InvocationResults
            {
              VoidReturnType = method.ReturnType == typeof(void),
              ReturnedObjectOrAddress = result is null ? ObjectOrRemoteAddress.Null
                : ObjectOrRemoteAddress.FromObj(result)
            }, owner);
          case "/get_field":
            var read = MessagePackSerializer.Deserialize<FieldGetRequest>(body);
            var readTarget = Resolve(read.ObjAddress);
            var readValue = readTarget.GetType().GetField(read.FieldName)!.GetValue(readTarget)!;
            LastMemberValue = (string)readValue;
            return Success(new InvocationResults
            { ReturnedObjectOrAddress = ObjectOrRemoteAddress.FromObj(readValue) }, owner);
          case "/set_field":
            var write = MessagePackSerializer.Deserialize<FieldSetRequest>(body);
            var writeTarget = Resolve(write.ObjAddress);
            var field = writeTarget.GetType().GetField(write.FieldName)!;
            field.SetValue(writeTarget, _runtime.ParseParameterObject(write.Value));
            var written = field.GetValue(writeTarget)!;
            LastMemberValue = (string)written;
            return Success(new InvocationResults
            { ReturnedObjectOrAddress = ObjectOrRemoteAddress.FromObj(written) }, owner);
          case "/create_object":
            var create = MessagePackSerializer.Deserialize<CtorInvocationRequest>(body);
            var created = Activator.CreateInstance(_runtime.ResolveType(create.TypeFullName),
              create.Parameters.Select(_runtime.ParseParameterObject).ToArray())!;
            ulong createdToken = _runtime.PinObject(created, Legacy ? null : _clients[owner]);
            return Success(new InvocationResults
            {
              ReturnedObjectOrAddress = ObjectOrRemoteAddress.FromToken(createdToken, created.GetType().FullName!)
            }, owner);
          case "/event_subscribe":
            var subscription = MessagePackSerializer.Deserialize<EventSubscriptionRequest>(body);
            if (Resolve(subscription.Address).GetType().GetEvent(subscription.EventName) is null)
              throw new InvalidOperationException("The requested event does not exist.");
            _capture = subscription.Capture?.Batch;
            return FailSubscribe ? MessagePackSerializer.Serialize(DiverResponse<object>.FromError("subscribe failed"))
              : Success(new EventRegistrationResults { Token = 1 }, owner);
          case "/hook_method":
            _capture = MessagePackSerializer.Deserialize<HookSubscriptionRequest>(body).Capture?.Batch;
            return Success(new EventRegistrationResults { Token = 1 }, owner);
          case "/event_property_filter":
          case "/event_unsubscribe":
          case "/unhook_method": return Success<object?>(null, owner);
          case "/unpin":
            var pin = MessagePackSerializer.Deserialize<ExpectedUnpin>(body);
            if (Legacy) _runtime.UnpinObject(pin.Address);
            else if (_clients.TryGetValue(owner, out int client))
              RequestOwner.Of(client).Unpin(_runtime, pin.Address, pin.PinOwner, owner);
            return Success<object?>(null, owner);
          case "/batch_execute":
            var batch = MessagePackSerializer.Deserialize<RemoteBatchRequest>(body);
            var executor = new BatchExecutor(_ => typeof(Item), input =>
              _runtime.TryGetPinnedObject(input.RemoteAddress, out var target) ? target
                : throw new InvalidOperationException("batch input was unpinned"));
            return Success(executor.Execute(batch), owner);
          default: throw new NotSupportedException(endpoint);
        }
      }
    }

    private byte[] DumpObject(byte[] body, string owner)
    {
      ObjectReads++;
      if (RejectRepeatedObjectReads && ObjectReads > 1)
        return MessagePackSerializer.Serialize(DiverResponse<object>.FromError("object lookup failed"));
      var request = MessagePackSerializer.Deserialize<ObjectDumpRequest>(body);
      _runtime.TryGetPinnedObject(request.Address, out var value);
      if (request.PinRequest && TakeObjectReference) _runtime.PinObject(value, Legacy ? null : _clients[owner]);
      return Success(new ObjectDump
      {
        Type = request.TypeName, PinnedAddress = request.Address,
        ReferenceTaken = request.PinRequest && TakeObjectReference
      }, owner);
    }

    private object Resolve(ulong token) =>
      _runtime.TryGetPinnedObject(token, out var value) ? value
        : throw new InvalidOperationException("the invocation input was unpinned");

    public string CaptureInputName()
    {
      lock (_lock)
      {
        var executor = new BatchExecutor(_ => typeof(Item), input => input.IsNull
          ? new PropertyChangedEventArgs("Name") : Resolve(input.RemoteAddress));
        return executor.Execute(_capture!).Values["name"].GetScalar<string>();
      }
    }

    public void Dispose()
    {
      using var runtime = _runtime;
      using var peer = _peer;
      using var client = Client;
      using var handle = Handle;
      using var router = Router;
      ContinueSubscribe?.TrySetResult(true);
      ContinueInvocation?.TrySetResult(true);
      ContinueUnpin?.TrySetResult(true);
      ContinueUnsubscribe?.TrySetResult(true);
      ContinueBatch?.TrySetResult(true);
      ContinueObject?.TrySetResult(true);
      ContinueUnregister?.TrySetResult(true);
    }
  }

  private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  private static async Task Until(Func<bool> predicate,
    [CallerArgumentExpression(nameof(predicate))] string? condition = null)
  {
    var watch = Stopwatch.StartNew();
    while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
    Assert.That(predicate(), Is.True, $"Timed out waiting for {condition}.");
  }

  private static void Collect()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }

  private static IPropertyChangeSubscription Subscribe(PinPeer peer, RemoteObject remote,
    RemoteBatch? capture = null)
  {
    remote.AddReference();
    return peer.Router.Subscribe((DynamicRemoteObject)remote.Dynamify(), capture, nameof(Item.Name));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference ExecuteAndDropBatchWithReleasedInput(PinPeer peer, out ulong token)
  {
    var input = CreateInputOwner(peer, useDynamicProxy: false);
    token = input.Token;
    return new WeakReference(input.Owner);
  }

  [Test]
  public async Task Test_CollectedBatch_ReleasesItsRemoteObjectInput()
  {
    using var peer = new PinPeer();
    var batch = ExecuteAndDropBatchWithReleasedInput(peer, out ulong token);
    Collect();
    Assert.That(batch.IsAlive, Is.False);
    await Until(() => !peer.IsPinned(token));
  }

  [Test]
  public async Task Test_PreviousRegistrationsWrapper_CannotReadOrReleaseCurrentPin()
  {
    using var peer = new PinPeer();
    var item = new Item();
    var old = peer.Add(item);
    ulong token = old.RemoteToken;
    Assert.That(peer.Client.UnregisterClient(), Is.True);
    Assert.That(peer.Client.RegisterClient(), Is.True);
    var current = peer.Handle.GetRemoteObjectFromField(peer.Pin(item), typeof(Item).FullName!);
    Assert.That(old.IsValid, Is.False);
    Assert.That(() => old.GetField(nameof(Item.Label)), Throws.TypeOf<ObjectDisposedException>());
    old.ReleaseReference();
    await DrainPinReturns(peer);
    Assert.That(PrimitivesEncoder.Decode(current.GetField(nameof(Item.Label))), Is.EqualTo("retained"));
    Assert.That(peer.Pins, Is.EqualTo(1), "the new registration's first pin is not surplus");
    current.ReleaseReference();
    await Until(() => !peer.IsPinned(token));
  }

  [TestCase(true)]
  [TestCase(false)]
  public async Task Test_LostUnregisterResponse_IsSettledBeforeNewRegistration(bool removed)
  {
    using var peer = new PinPeer { RemoveBeforeDrop = removed };
    var item = new Item();
    var old = peer.Add(item);
    string original = peer.LastRegistration!;
    peer.LoseUnregisterResponse = true;
    Assert.That(peer.Client.TryUnregisterClient(TimeSpan.FromSeconds(1)), Is.False);
    Assert.That(old.IsValid, Is.False);
    Assert.That(peer.Client.RegisterClient(), Is.True);
    Assert.That(peer.LastRegistration, Is.Not.EqualTo(original));
    var current = peer.Handle.GetRemoteObjectFromField(peer.Pin(item), typeof(Item).FullName!);
    old.ReleaseReference();
    await DrainPinReturns(peer);
    Assert.That(PrimitivesEncoder.Decode(current.GetField(nameof(Item.Label))), Is.EqualTo("retained"));
    Assert.That(peer.Pins, Is.EqualTo(1));
    current.ReleaseReference();
    await Until(() => peer.Pins == 0);
  }

  [Test]
  public async Task Test_QueuedRelease_CannotConsumeANewRegistrationsReference()
  {
    using var peer = new PinPeer();
    var item = new Item();
    var remote = peer.Add(item);
    ulong token = remote.RemoteToken;
    var scheduler = (ChannelScheduler)typeof(DiverCommunicator)
      .GetField("_releaseScheduler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer.Client)!;
    var entered = Signal();
    var proceed = Signal();
    scheduler.Enqueue(async () => { entered.SetResult(true); await proceed.Task; });
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      remote.ReleaseReference();
      Assert.That(peer.Client.UnregisterClient(), Is.True);
      Assert.That(peer.Client.RegisterClient(), Is.True);
      Assert.That(peer.Pin(item), Is.EqualTo(token));
      var drained = Signal();
      scheduler.Enqueue(() => drained.SetResult(true));
      proceed.SetResult(true);
      await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(peer.Pins, Is.EqualTo(1), "the queued release belongs to the removed registration");
      peer.Client.UnpinObject(token);
      Assert.That(peer.IsPinned(token), Is.False);
    }
    finally
    {
      proceed.TrySetResult(true);
    }
  }

  [Test]
  public async Task Test_LateObjectReceipt_DoesNotReturnTheNewRegistrationsPin()
  {
    using var peer = new PinPeer { ObjectEntered = Signal(), ContinueObject = Signal() };
    var item = new Item();
    ulong token = peer.Pin(item);
    var reading = Task.Run(() => peer.Handle.GetRemoteObject(token, typeof(Item).FullName!));
    await peer.ObjectEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(peer.Client.UnregisterClient(), Is.True);
    Assert.That(peer.Client.RegisterClient(), Is.True);
    Assert.That(peer.Pin(item), Is.EqualTo(token));
    peer.ContinueObject.SetResult(true);
    var current = await reading.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(current.IsValid, Is.True, "retrying acquired a receipt from the current registration");
    current.ReleaseReference();
    await DrainPinReturns(peer);
    Assert.That(peer.Pins, Is.EqualTo(1));
    Assert.That(peer.IsPinned(token), Is.True, "the new registration's independent pin remains");
    peer.Client.UnpinObject(token);
    Assert.That(peer.IsPinned(token), Is.False);
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task Test_ReleaseAfterConnectionLoss_ReturnsTheOriginalRegistrationsPin(bool delayed)
  {
    using var peer = new PinPeer();
    var remote = peer.Add(new Item());
    ulong token = remote.RemoteToken;
    string owner = peer.LastRegistration!;
    peer.DropConnection();
    await Until(() => !peer.Client.IsConnected);
    remote.ReleaseReference(useJitter: delayed);
    await Until(() => !peer.IsPinned(token));
    Assert.That(peer.LastRegistration, Is.EqualTo(owner), "reconnecting retains the original registration");
  }

  [Test]
  public async Task Test_TimedOutUnregister_CannotRemoveALaterRegistration()
  {
    using var peer = new PinPeer
    {
      DelayNextUnregister = true, UnregisterEntered = Signal(),
      ContinueUnregister = Signal(), DelayedUnregisterCompleted = Signal()
    };
    var item = new Item();
    ulong token = peer.Pin(item);
    var unregistering = Task.Run(() => peer.Client.TryUnregisterClient(TimeSpan.FromMilliseconds(300)));
    await peer.UnregisterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(await unregistering, Is.False);
    Assert.That(peer.Client.IsConnected, Is.True, "only the request timed out");
    Assert.That(peer.Client.RegisterClient(), Is.True, "pending cleanup completed before registering again");
    Assert.That(peer.Pin(item), Is.EqualTo(token));
    peer.ContinueUnregister.SetResult(true);
    await peer.DelayedUnregisterCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(peer.Pins, Is.EqualTo(1), "the delayed old unregister cannot remove the fresh registration");
    peer.Client.UnpinObject(token);
    Assert.That(peer.IsPinned(token), Is.False);
  }

  [TestCase(true, TestName = "Test_TransientMetadataFailure_ReturnsOnlyAcquiredReferences")]
  [TestCase(false, TestName = "Test_PersistentMetadataFailure_ReturnsOnlyAcquiredReferences")]
  public async Task Test_MetadataFailure_PreservesExistingPin(bool recover)
  {
    int reads = 0;
    using var peer = new PinPeer { RejectTypeRead = () => !recover || reads++ == 0 };
    ulong token = peer.Pin(new RetryItem());
    if (!recover)
    {
      var error = Assert.Throws<InvalidOperationException>(() =>
        peer.Handle.GetRemoteObject(token, typeof(RetryItem).FullName!));
      Assert.That(error!.InnerException, Is.TypeOf<MTGOSDK.Core.Exceptions.ExternalErrorException>());
      Assert.That(error.InnerException!.Message, Is.EqualTo("metadata unavailable"));
    }
    else
      peer.Handle.GetRemoteObject(token, typeof(RetryItem).FullName!).ReleaseReference();
    await DrainPinReturns(peer);
    Assert.That(peer.Pins, Is.EqualTo(1));
    Assert.That(peer.IsPinned(token), Is.True, "the independently owned original pin is intact");
    peer.Client.UnpinObject(token);
    Assert.That(peer.IsPinned(token), Is.False);
  }

  [TestCase(false, false, "metadata unavailable",
    TestName = "Test_MetadataFailureWithoutAcquisition_PreservesExistingPin")]
  [TestCase(true, true, "object lookup failed",
    TestName = "Test_FailedObjectRetry_DoesNotReleaseEarlierAcquisitionTwice")]
  public async Task Test_FailedObjectReads_PreserveExistingPin(
    bool takeReference, bool rejectRepeated, string expectedError)
  {
    using var peer = new PinPeer
    {
      RejectTypeRead = () => true, TakeObjectReference = takeReference,
      RejectRepeatedObjectReads = rejectRepeated
    };
    ulong token = peer.Pin(new RetryItem());
    var error = Assert.Throws<InvalidOperationException>(() =>
      peer.Handle.GetRemoteObject(token, typeof(RetryItem).FullName!));
    Assert.That(error!.InnerException, Is.TypeOf<MTGOSDK.Core.Exceptions.ExternalErrorException>());
    Assert.That(error.InnerException!.Message, Is.EqualTo(expectedError));
    await DrainPinReturns(peer);
    Assert.That(peer.Pins, Is.EqualTo(1));
    Assert.That(peer.IsPinned(token), Is.True, "no failed attempt owns the caller's original pin");
    peer.Client.UnpinObject(token);
    Assert.That(peer.IsPinned(token), Is.False);
  }


  private static async Task DrainPinReturns(PinPeer peer)
  {
    var scheduler = (ChannelScheduler)typeof(DiverCommunicator)
      .GetField("_releaseScheduler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer.Client)!;
    var drained = Signal();
    scheduler.Enqueue(() => drained.SetResult(true));
    await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static IDisposable SubscribeWithTemporaryCapture(PinPeer peer, RemoteObject target)
  {
    var input = (DynamicRemoteObject)peer.Add(new Item()).Dynamify();
    var batch = new RemoteBatch();
    batch.Output("name", batch.Read(batch.Input((object)input), "Name"));
    return Subscribe(peer, target, batch);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static object InvokeWithTemporaryInputs(PinPeer peer, string api)
  {
    var argument = (DynamicRemoteObject)peer.Add(new Item { Name = "argument" }).Dynamify();
    if (api == "RemoteObject.InvokeMethod")
    {
      var raw = peer.Add(new Item());
      var (_, value) = raw.InvokeMethod(nameof(Item.Echo),
        RemoteFunctionsInvokeHelper.CreateRemoteParameter(argument));
      return PrimitivesEncoder.Decode(value);
    }
    var target = (DynamicRemoteObject)peer.Add(new Item { Name = "receiver" }).Dynamify();
    switch (api)
    {
      case "DynamicRemoteObject.InvokeMethod":
        return target.InvokeMethod<string>(nameof(Item.Echo), argument);
      case "dynamic invocation":
        return ((dynamic)target).Echo(argument);
      case "DynamicRemoteMethod.TryInvoke":
        var method = new DynamicRemoteObject.DynamicRemoteMethod(nameof(Item.Echo),
          target.__type.Methods.Where(m => m.Name == nameof(Item.Echo)).ToList());
        Assert.That(method.TryInvoke(target, [argument], out object result), Is.True);
        return result;
      case "RemoteFunctionsInvokeHelper.Invoke":
        return RemoteFunctionsInvokeHelper.Invoke(peer.Handle, typeof(Item), nameof(Item.Echo),
          target, Array.Empty<string>(), [argument]);
      default:
        throw new ArgumentOutOfRangeException(nameof(api), api, "Unknown invocation API.");
    }
  }

  [TestCase("RemoteFunctionsInvokeHelper.Invoke")]
  [TestCase("DynamicRemoteObject.InvokeMethod")]
  [TestCase("dynamic invocation")]
  [TestCase("DynamicRemoteMethod.TryInvoke")]
  [TestCase("RemoteObject.InvokeMethod")]
  public async Task Test_Invocation_KeepsTemporaryInputsUntilResponse(string api)
  {
    using var peer = new PinPeer { InvocationEntered = Signal(), ContinueInvocation = Signal() };
    var invoking = Task.Run(() => InvokeWithTemporaryInputs(peer, api));
    await peer.InvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Collect();
      await DrainPinReturns(peer);
      peer.ContinueInvocation.SetResult(true);
      Assert.That(await invoking.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("argument"));
    }
    finally
    {
      peer.ContinueInvocation.TrySetResult(true);
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static RemoteObject ConstructWithTemporaryArgument(PinPeer peer)
  {
    var argument = (DynamicRemoteObject)peer.Add(new Item { Name = "argument" }).Dynamify();
    return peer.Handle.Activator.CreateInstance(typeof(Item), argument);
  }

  [Test]
  public async Task Test_Construction_KeepsTemporaryArgumentUntilResponse()
  {
    using var peer = new PinPeer { InvocationEntered = Signal(), ContinueInvocation = Signal() };
    var constructing = Task.Run(() => ConstructWithTemporaryArgument(peer));
    await peer.InvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Collect();
      await DrainPinReturns(peer);
      peer.ContinueInvocation.SetResult(true);
      var created = await constructing.WaitAsync(TimeSpan.FromSeconds(5));
      try
      {
        var (_, name) = created.InvokeMethod("get_" + nameof(Item.Name));
        Assert.That(PrimitivesEncoder.Decode(name), Is.EqualTo("argument"),
          "the constructor reads its argument and returns a usable remote object");
      }
      finally
      {
        created.ReleaseReference();
      }
    }
    finally
    {
      peer.ContinueInvocation.TrySetResult(true);
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (object Owner, ulong Token) CreateInputOwner(PinPeer peer, bool useDynamicProxy)
  {
    var remote = peer.Add(new Item());
    ulong token = remote.RemoteToken;
    if (useDynamicProxy)
    {
      var proxy = (DynamicRemoteObject)remote.Dynamify();
      Assert.That((string)((dynamic)proxy).Name, Is.EqualTo("retained"));
      return (proxy, token);
    }
    var batch = new RemoteBatch();
    batch.Output("name", batch.Read(batch.Input((object)remote), "Name"));
    remote.ReleaseReference();
    Assert.That(batch.Execute(peer.Client).Values["name"].GetScalar<string>(), Is.EqualTo("retained"));
    return (batch, token);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Owner, ulong Token) DropInputOwnerDuringDisconnect(PinPeer peer, bool useDynamicProxy)
  {
    var input = CreateInputOwner(peer, useDynamicProxy);
    peer.DropConnection();
    Assert.That(SpinWait.SpinUntil(() => !peer.Client.IsConnected, TimeSpan.FromSeconds(5)), Is.True);
    var result = (new WeakReference(input.Owner), input.Token);
    GC.KeepAlive(input.Owner);
    return result;
  }

  [TestCase(false, TestName = "Test_CollectedBatchInput_ReturnsPinAfterReconnect")]
  [TestCase(true, TestName = "Test_CollectedDynamicProxy_ReturnsPinAfterReconnect")]
  public async Task Test_CollectedInputOwnerDuringDisconnect_ReturnsPinAfterReconnect(bool useDynamicProxy)
  {
    using var peer = new PinPeer();
    var input = DropInputOwnerDuringDisconnect(peer, useDynamicProxy);
    Collect();
    Assert.That(input.Owner.IsAlive, Is.False);
    await peer.Client.ConnectAsync();
    await Until(() => !peer.IsPinned(input.Token));
  }

  [Test]
  public async Task Test_UnansweredUnpin_DoesNotBlockReleaseOrUnregister()
  {
    using var peer = new PinPeer { UnpinEntered = Signal(), ContinueUnpin = Signal() };
    var remote = peer.Add(new Item());
    var releasing = Task.Run(() => remote.ReleaseReference());
    await peer.UnpinEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      bool released = await Task.WhenAny(releasing, Task.Delay(500)) == releasing;
      bool unregistered = peer.Client.TryUnregisterClient(TimeSpan.FromSeconds(2));
      Assert.Multiple(() =>
      {
        Assert.That(released, Is.True, "release does not wait for the peer's answer");
        Assert.That(unregistered, Is.True, "the pending unpin does not prevent unregistering");
      });
    }
    finally
    {
      peer.ContinueUnpin.TrySetResult(true);
      await releasing.WaitAsync(TimeSpan.FromSeconds(5));
    }
  }

  [Test]
  public async Task Test_OlderDiver_UnregisterStillAllowsExplicitCleanup()
  {
    using var peer = new PinPeer(legacy: true);
    var standalone = peer.Add(new Item());
    var target = peer.Add(new Item());
    ulong standaloneToken = standalone.RemoteToken;
    ulong targetToken = target.RemoteToken;
    var subscription = Subscribe(peer, target);
    Collect();
    Assert.That(peer.Client.UnregisterClient(), Is.True);
    standalone.ReleaseReference();
    subscription.Dispose();
    target.ReleaseReference();
    Assert.That(peer.Requests, Does.Contain("/event_unsubscribe"));
    await Until(() => !peer.IsPinned(standaloneToken) && !peer.IsPinned(targetToken));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static Task<RemoteBatchResult> ExecuteTemporaryBatch(PinPeer peer, bool detached)
  {
    var input = (DynamicRemoteObject)peer.Add(new Item()).Dynamify();
    var batch = new RemoteBatch();
    batch.Output("name", batch.Read(batch.Input((object)input), "Name"));
    return detached ? peer.Client.ExecuteBatchAsync(batch.ToRequest()) : batch.ExecuteAsync(peer.Client);
  }

  [TestCase(false, TestName = "Test_BatchExecuteAsync_KeepsInputsUntilResponse")]
  [TestCase(true, TestName = "Test_ExecuteBatchAsync_KeepsRequestInputsUntilResponse")]
  public async Task Test_BatchExecution_KeepsTemporaryInputsUntilResponse(bool detached)
  {
    using var peer = new PinPeer { BatchEntered = Signal(), ContinueBatch = Signal() };
    var executing = ExecuteTemporaryBatch(peer, detached);
    await peer.BatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Collect();
      await DrainPinReturns(peer);
      peer.ContinueBatch.SetResult(true);
      var result = await executing.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(result.Values["name"].GetScalar<string>(), Is.EqualTo("retained"));
    }
    finally
    {
      peer.ContinueBatch.TrySetResult(true);
    }
  }

  [Test]
  public async Task Test_PendingUnsubscribe_KeepsCaptureInputsUntilResponse()
  {
    using var peer = new PinPeer { UnsubscribeEntered = Signal(), ContinueUnsubscribe = Signal() };
    var target = peer.Add(new Item());
    var subscription = SubscribeWithTemporaryCapture(peer, target);
    Collect();
    await DrainPinReturns(peer);
    Assert.That(peer.CaptureInputName(), Is.EqualTo("retained"),
      "the active capture can still read its input after the original batch is collected");
    var disposing = Task.Run(subscription.Dispose);
    await peer.UnsubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Collect();
      await DrainPinReturns(peer);
      Assert.That(peer.CaptureInputName(), Is.EqualTo("retained"));
    }
    finally
    {
      peer.ContinueUnsubscribe.TrySetResult(true);
      await disposing.WaitAsync(TimeSpan.FromSeconds(5));
    }
    Collect();
    await DrainPinReturns(peer);
    Assert.That(() => peer.CaptureInputName(), Throws.TypeOf<InvalidOperationException>());
    target.ReleaseReference();
    GC.KeepAlive(subscription);
  }

  [Test]
  public async Task Test_UnansweredUnpin_TimesOutWithoutReplayingTheRelease()
  {
    using var peer = new PinPeer { UnpinEntered = Signal(), ContinueUnpin = Signal() };
    var remote = peer.Add(new Item());
    ulong token = remote.RemoteToken;
    remote.ReleaseReference();
    await peer.UnpinEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      await Until(() => peer.Client.PendingRequestCount == 0);
      Assert.That(peer.Requests.Count(p => p == "/unpin"), Is.EqualTo(1));
      Assert.That(peer.IsPinned(token), Is.True, "a timeout does not confirm that the peer released the pin");
      Assert.That(peer.Client.TryUnregisterClient(TimeSpan.FromSeconds(2)), Is.True);
      Assert.That(peer.IsPinned(token), Is.False, "unregistering reclaims the remaining reference");
    }
    finally
    {
      peer.ContinueUnpin.TrySetResult(true);
    }
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task Test_OlderDiver_KeepsSharedOwnershipAcrossReregister(bool reconnect)
  {
    using var peer = new PinPeer(legacy: true);
    var item = new Item();
    var remote = peer.Add(item);
    ulong token = remote.RemoteToken;
    Assert.That(peer.Client.UnregisterClient(), Is.True);
    if (reconnect)
    {
      peer.DropConnection();
      await Until(() => !peer.Client.IsConnected);
    }
    Assert.That(peer.Client.RegisterClient(), Is.True);
    Assert.That(remote.IsValid, Is.True);
    remote.ReleaseReference();
    await Until(() => !peer.IsPinned(token));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateLazyWrapper(Action initialize)
  {
    var lazy = new LazyRemoteObject();
    lazy.Set(() => { initialize(); return null!; });
    return new WeakReference(lazy);
  }

  [Test]
  public void Test_CollectingLazyWrapper_DoesNotInitializeIt()
  {
    int initialized = 0;
    var lazy = CreateLazyWrapper(() => initialized++);
    Collect();
    Assert.That(lazy.IsAlive, Is.False);
    Assert.That(initialized, Is.Zero);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateResolvedLazyWrapper(DynamicRemoteObject proxy)
  {
    var lazy = new LazyRemoteObject();
    lazy.Set(() => proxy);
    lazy.GetResolvedInstance();
    return new WeakReference(lazy);
  }

  [Test]
  public async Task Test_CollectingResolvedLazyWrapper_DoesNotReleaseAnotherWrappersOwnership()
  {
    using var peer = new PinPeer();
    var proxy = (DynamicRemoteObject)peer.Add(new Item()).Dynamify();
    var lazy = CreateResolvedLazyWrapper(proxy);
    Collect();
    await DrainPinReturns(peer);
    Assert.That(lazy.IsAlive, Is.False);
    Assert.That((string)((dynamic)proxy).Name, Is.EqualTo("retained"));
    Assert.That(peer.IsPinned(proxy.__ro.RemoteToken), Is.True);
    GC.KeepAlive(proxy);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference RegisterTemporaryCapture(PinPeer peer, bool hook = false,
    Action<WeakReference>? recorded = null)
  {
    var input = (DynamicRemoteObject)peer.Add(new Item()).Dynamify();
    var batch = new RemoteBatch();
    batch.Output("name", batch.Read(batch.Input((object)input), "Name"));
    var capture = new RemoteEventCapture { Batch = batch.ToRequest() };
    var callbackState = new object();
    var stateReference = new WeakReference(callbackState);
    recorded?.Invoke(stateReference);
    if (hook)
      peer.Client.HookMethod(typeof(Item).FullName!, nameof(Item.Echo), HarmonyPatchPosition.Postfix,
        (_, _, _) => GC.KeepAlive(callbackState), capture: new RemoteHookCapture { Batch = capture.Batch });
    else
      peer.Client.EventSubscribe(input.__ro.RemoteToken, "PropertyChanged",
        (_, _) => GC.KeepAlive(callbackState), capture);
    return stateReference;
  }

  [TestCase(false, TestName = "Test_DisposingCommunicator_ReleasesEventCallbackState")]
  [TestCase(true, TestName = "Test_DisposingCommunicator_ReleasesHookCallbackState")]
  public void Test_DisposingCommunicator_ReleasesCallbackState(bool hook)
  {
    using var peer = new PinPeer();
    var callbackState = RegisterTemporaryCapture(peer, hook);
    Collect();
    Assert.That(callbackState.IsAlive, Is.True, "the active subscription retains its callback state");
    peer.Client.Dispose();
    Collect();
    Assert.That(callbackState.IsAlive, Is.False, "disposal releases caller-owned callback state");
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static object? AccessTemporaryTarget(PinPeer peer, string api)
  {
    var remote = peer.Add(new Item());
    if (api == "RemoteObject.GetField") return PrimitivesEncoder.Decode(remote.GetField(nameof(Item.Label)));
    if (api == "RemoteObject.SetField") return PrimitivesEncoder.Decode(remote.SetField(nameof(Item.Label),
      ObjectOrRemoteAddress.FromObj("changed")));
    var target = (DynamicRemoteObject)remote.Dynamify();
    if (api == "dynamic property assignment")
    {
      ((dynamic)target).Name = "changed";
      return "changed";
    }
    var field = target.__type.GetField(nameof(Item.Label))!;
    switch (api)
    {
      case "RemoteFieldInfo.GetValue":
        return field.GetValue(target);
      case "RemoteFieldInfo.SetValue":
        field.SetValue(target, "changed");
        return "changed";
      default:
        throw new ArgumentOutOfRangeException(nameof(api), api, "Unknown member access API.");
    }
  }

  [TestCase("RemoteObject.GetField", "retained")]
  [TestCase("RemoteObject.SetField", "changed")]
  [TestCase("RemoteFieldInfo.GetValue", "retained")]
  [TestCase("RemoteFieldInfo.SetValue", "changed")]
  [TestCase("dynamic property assignment", "changed")]
  public async Task Test_MemberAccess_KeepsTemporaryTargetUntilResponse(string api, string expected)
  {
    using var peer = new PinPeer { InvocationEntered = Signal(), ContinueInvocation = Signal() };
    var accessing = Task.Run(() => AccessTemporaryTarget(peer, api));
    await peer.InvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Collect();
      await DrainPinReturns(peer);
      peer.ContinueInvocation.SetResult(true);
      Assert.That(await accessing.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(expected));
      Assert.That(peer.LastMemberValue, Is.EqualTo(expected), "the request observes the expected remote state");
    }
    finally
    {
      peer.ContinueInvocation.TrySetResult(true);
    }
  }

  [Test]
  public async Task Test_FailedPropertySubscription_ReturnsOnlyItsOwnReferences()
  {
    using var peer = new PinPeer { FailSubscribe = true };
    var target = peer.Add(new Item());
    ulong token = target.RemoteToken;

    Assert.That(() => SubscribeWithTemporaryCapture(peer, target),
      Throws.InstanceOf<MTGOSDK.Core.Exceptions.ExternalErrorException>().With.Message.EqualTo("subscribe failed"));
    Collect();
    await DrainPinReturns(peer);

    Assert.That(PrimitivesEncoder.Decode(target.GetField(nameof(Item.Label))), Is.EqualTo("retained"),
      "failure preserves the caller's target reference");
    Assert.That(() => peer.CaptureInputName(), Throws.TypeOf<InvalidOperationException>()
      .With.Message.EqualTo("the invocation input was unpinned"), "failure releases the temporary capture input");
    target.ReleaseReference();
    await Until(() => !peer.IsPinned(token));
  }

  [Test]
  public async Task Test_DisposalDuringEventSubscription_ReleasesPendingCallbackState()
  {
    using var peer = new PinPeer { SubscribeEntered = Signal(), ContinueSubscribe = Signal() };
    var recorded = new TaskCompletionSource<WeakReference>(TaskCreationOptions.RunContinuationsAsynchronously);
    var subscribing = Task.Run(() => RegisterTemporaryCapture(peer,
      recorded: owner => recorded.SetResult(owner)));
    var callbackState = await recorded.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await peer.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Task.Run(peer.Client.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
    await Assert.ThatAsync(() => subscribing.WaitAsync(TimeSpan.FromSeconds(5)),
      Throws.InstanceOf<OperationCanceledException>());
    Collect();
    Assert.That(callbackState.IsAlive, Is.False, "failed subscription state is released after disposal");
  }

  [TestCase(false, TestName = "Test_DisposedCommunicator_AllowsLateEventUnsubscribe")]
  [TestCase(true, TestName = "Test_DisposedCommunicator_AllowsLateUnhook")]
  public void Test_DisposedCommunicator_AllowsLateSubscriptionCleanup(bool hook)
  {
    using var peer = new PinPeer();
    DiverCommunicator.LocalEventCallback callback = (_, _) => { };
    LocalHookCallback hookCallback = (_, _, _) => { };
    if (hook)
      peer.Client.HookMethod(typeof(Item).FullName!, nameof(Item.Echo),
        HarmonyPatchPosition.Postfix, hookCallback);
    else
    {
      var target = peer.Add(new Item());
      peer.Client.EventSubscribe(target.RemoteToken, "PropertyChanged", callback);
      GC.KeepAlive(target);
    }
    peer.Client.Dispose();
    var requests = peer.Requests;
    Assert.DoesNotThrow(() =>
    {
      if (hook) peer.Client.UnhookMethod(hookCallback);
      else peer.Client.EventUnsubscribe(callback);
    });
    Assert.That(peer.Requests, Is.EqualTo(requests), "late cleanup sends nothing on a disposed connection");
  }
}
