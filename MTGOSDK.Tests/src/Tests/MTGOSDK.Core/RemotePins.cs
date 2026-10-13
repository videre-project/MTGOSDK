/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Types;


namespace MTGOSDK.Tests.MTGOSDK_Core;

/// <summary>
/// Pin lifetimes in the Diver: what a client acquires stays pinned while it
/// holds it, and is released when it lets go, unregisters or its process exits.
/// </summary>
/// <remarks>
/// Each check reads one token, or one client's own pin count, so the tests do
/// not depend on the pins other tests take in parallel. A second client is a
/// DiverCommunicator registered under the process id of a stand-in process.
/// </remarks>
[NonParallelizable]
public class RemotePins : BaseFixture
{
  private const string ListType = "System.Collections.ArrayList";

  private static DiverCommunicator Communicator =>
    RemoteClient.@client!.Communicator;

  /// <summary>Whether the Diver still holds the ArrayList behind a token.</summary>
  private static bool IsPinned(ulong token)
  {
    try
    {
      Communicator.GetField(token, ListType, "_size");
      return true;
    }
    catch (ExternalErrorException e) when (e.Message.Contains("unpinned"))
    {
      return false;
    }
  }

  /// <summary>The pin references a registered client holds.</summary>
  private static int ClientPins(int processId) =>
    RemoteClient.GetDiverDiagnostics()!.ClientPinReferences!.TryGetValue(processId, out int count)
      ? count
      : 0;

  private static async Task<bool> Eventually(Func<bool> condition, TimeSpan timeout)
  {
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < timeout)
    {
      // Release this process's dropped proxies (their finalizers queue the unpins).
      GC.Collect();
      GC.WaitForPendingFinalizers();
      if (condition()) return true;
      await Task.Delay(500);
    }
    return false;
  }

  private static ulong Token(dynamic remote) =>
    ((DynamicRemoteObject)remote).__ro.RemoteToken;

  /// <summary>A process whose id a second client registers with.</summary>
  private static Process StartStandIn() =>
    Process.Start(new ProcessStartInfo("ping", "-n 120 127.0.0.1")
    {
      CreateNoWindow = true,
      UseShellExecute = false,
    })!;

  private static DiverCommunicator ConnectOther(Process standIn)
  {
    var other = new DiverCommunicator("127.0.0.1", RemoteClient.Port!.Value);
    Assert.That(other.RegisterClient(standIn.Id), Is.True);
    return other;
  }

  /// <summary>Closes the second client's connection (without unregistering).</summary>
  private static void Close(DiverCommunicator other) => other.Dispose();

  private static FieldInfo PrivateField(Type type, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

  /// <summary>
  /// A second client that speaks as an older client does: it registers
  /// without a client id and never registers a reconnected connection again.
  /// </summary>
  private static DiverCommunicator ConnectOlder(Process standIn)
  {
    var other = new DiverCommunicator("127.0.0.1", RemoteClient.Port!.Value);
    PrivateField(typeof(DiverCommunicator), "_clientId").SetValue(other, null);
    Assert.That(other.RegisterClient(standIn.Id), Is.True);
    PrivateField(typeof(DiverCommunicator), "_registered").SetValue(other, false);
    return other;
  }

  /// <summary>
  /// Drops a client's connection as a network failure would, without
  /// disposing the client; its next request connects again.
  /// </summary>
  private static void DropConnection(DiverCommunicator client)
  {
    var tcp = PrivateField(typeof(DiverCommunicator), "_tcp").GetValue(client)!;
    var session = PrivateField(typeof(TcpCommunicator), "_session").GetValue(tcp)!;
    var socket = (TcpClient)PrivateField(session.GetType(), "_client").GetValue(session)!;
    socket.Client.Shutdown(SocketShutdown.Both);
    socket.Close();
  }

  /// <summary>
  /// An older SDK's liveness probe: a connection that never registers, has
  /// pinged once and ignores every callback (it returns no reference).
  /// </summary>
  private static DiverCommunicator ConnectOlderProbe()
  {
    var probe = new DiverCommunicator("127.0.0.1", RemoteClient.Port!.Value);
    var tcp = (TcpCommunicator)PrivateField(typeof(DiverCommunicator), "_tcp").GetValue(probe)!;
    tcp.SetCallbackHandler((_, _) => { });
    Assert.That(probe.CheckAliveness(), Is.True);
    return probe;
  }

  /// <summary>Whether the Diver still holds the object behind a token.</summary>
  private static bool IsPinned(ulong token, string typeName)
  {
    try
    {
      Communicator.DumpObject(token, typeName, pinObject: false);
      return true;
    }
    catch (ExternalErrorException)
    {
      return false;
    }
  }

  [Test]
  public void Test_NullFieldValue_IsReadAsNull()
  {
    // A new ArrayList's lazily created SyncRoot is still null.
    dynamic list = RemoteClient.CreateInstance(ListType);
    var value = ((DynamicRemoteObject)list).__ro.GetField("_syncRoot");
    Assert.That(value.IsNull, Is.True);

    var field = RemoteClient.GetInstanceType(ListType)
      .GetField("_syncRoot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Assert.That(field.GetValue((object)list), Is.Null);
  }

  [Test]
  public void Test_NonPublicStaticField_IsRead()
  {
    // ArrayList's private static empty array.
    var value = Communicator.GetField(0, ListType, "emptyArray").ReturnedObjectOrAddress!;
    Assert.That(value.IsNull, Is.False);
    Assert.That(value.IsRemoteAddress, Is.True);
    Communicator.UnpinObject(value.RemoteAddress);
  }

  [Test]
  public async Task Test_ChildProxy_IsNotRetainedByItsParent()
  {
    dynamic parent = RemoteClient.CreateInstance(ListType);
    parent.Add(RemoteClient.CreateInstance(ListType));

    ulong childToken = ReadChildToken(parent);
    Assert.That(IsPinned(childToken), Is.True);

    // The parent stays alive and keeps its element; only the proxy was dropped.
    Assert.That(await Eventually(() => !IsPinned(childToken), TimeSpan.FromSeconds(45)), Is.True,
      "a dropped child proxy is released while its parent proxy lives");
    Assert.That(IsPinned(Token(parent)), Is.True);
    GC.KeepAlive(parent);
  }

  /// <summary>A separate frame, so no local keeps the child proxy reachable.</summary>
  private static ulong ReadChildToken(dynamic parent)
  {
    dynamic child = parent[0];
    return ((DynamicRemoteObject)child).__ro.RemoteToken;
  }

  [Test]
  public async Task Test_CallbackArguments_ArePinnedForEachRecipient()
  {
    using var standIn = StartStandIn();
    var other = ConnectOther(standIn);
    try
    {
      // The second client subscribes; both registered clients receive the
      // callback, each with its own reference to the sender and the arguments.
      dynamic component = RemoteClient.CreateInstance("System.ComponentModel.Component");
      var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
      DiverCommunicator.LocalEventCallback handler = (context, args) =>
      {
        int held = ClientPins(standIn.Id);
        foreach (var argument in args)
          if (argument.IsRemoteAddress && !argument.IsNull)
            other.UnpinObject(argument.RemoteAddress);
        delivered.TrySetResult(held);
      };
      other.EventSubscribe(Token(component), "Disposed", handler);
      component.Dispose();

      Assert.That(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(2),
        "the second client holds one reference to the sender and one to the event arguments");
      Assert.That(await Eventually(() => ClientPins(standIn.Id) == 0, TimeSpan.FromSeconds(10)), Is.True,
        "its releases return exactly its own references");
      other.EventUnsubscribe(handler);
    }
    finally
    {
      Close(other);
      standIn.Kill();
    }
  }

  [Test]
  public async Task Test_ExitedClient_PinsAreReclaimed()
  {
    using var standIn = StartStandIn();
    DiverCommunicator? other = ConnectOther(standIn);
    try
    {
      ulong token = other.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;
      Assert.That(IsPinned(token), Is.True);
      Assert.That(ClientPins(standIn.Id), Is.EqualTo(1));

      // Another client cannot release it.
      Communicator.UnpinObject(token);
      Assert.That(IsPinned(token), Is.True, "only the client that pinned it releases it");

      // Its connection closes while its process still runs: the pin is kept.
      Close(other);
      other = null;
      await Task.Delay(TimeSpan.FromSeconds(12));
      Assert.That(IsPinned(token), Is.True, "a client whose process runs may reconnect");

      // Its process exits without unregistering: the pin is reclaimed.
      standIn.Kill();
      standIn.WaitForExit();
      Assert.That(await Eventually(() => !IsPinned(token), TimeSpan.FromSeconds(30)), Is.True,
        "an exited client's pins are released");
    }
    finally
    {
      if (other != null) Close(other);
      if (!standIn.HasExited) standIn.Kill();
    }
  }

  [Test]
  public void Test_UnregisteringClient_ReleasesItsPins()
  {
    using var standIn = StartStandIn();
    var other = ConnectOther(standIn);
    try
    {
      ulong token = other.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;
      Assert.That(IsPinned(token), Is.True);

      Assert.That(other.UnregisterClient(standIn.Id), Is.True);
      Assert.That(IsPinned(token), Is.False);
    }
    finally
    {
      Close(other);
      standIn.Kill();
    }
  }

  [Test]
  public void Test_DisposingClient_ReleasesItsPinsAtOnce()
  {
    using var standIn = StartStandIn();
    var other = ConnectOther(standIn);
    try
    {
      ulong token = other.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;
      Assert.That(IsPinned(token), Is.True);

      // As RemoteClient.Dispose does, while the client's process keeps running.
      Assert.That(other.TryUnregisterClient(TimeSpan.FromSeconds(2)), Is.True);
      Assert.That(IsPinned(token), Is.False);
    }
    finally
    {
      Close(other);
      standIn.Kill();
    }
  }

  [Test]
  public void Test_ClientsInOneProcess_AreSeparate()
  {
    using var standIn = StartStandIn();
    var first = ConnectOther(standIn);
    var second = ConnectOther(standIn);
    try
    {
      ulong kept = second.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;
      ulong released = first.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;

      Assert.That(first.UnregisterClient(standIn.Id), Is.True);
      Assert.That(IsPinned(released), Is.False);
      Assert.That(IsPinned(kept), Is.True, "the other client of the same process keeps its pin");
      second.UnpinObject(kept);
      Assert.That(IsPinned(kept), Is.False);
    }
    finally
    {
      Close(first);
      Close(second);
      standIn.Kill();
    }
  }

  [Test]
  public async Task Test_RequestedPin_OfAnotherClientsObject_IsOwnReference()
  {
    using var standIn = StartStandIn();
    DiverCommunicator? other = ConnectOther(standIn);
    try
    {
      ulong token = other.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;

      // This client asks for its own pin on the object the other client holds.
      var dump = Communicator.DumpObject(token, ListType, pinObject: true);
      Assert.That(dump.PinnedAddress, Is.EqualTo(token));
      Assert.That(dump.ReferenceTaken, Is.True);

      // The other client exits: this client's reference keeps the object.
      Close(other);
      other = null;
      standIn.Kill();
      standIn.WaitForExit();
      await Task.Delay(TimeSpan.FromSeconds(12));
      Assert.That(IsPinned(token), Is.True);

      Communicator.UnpinObject(token);
      Assert.That(IsPinned(token), Is.False);
    }
    finally
    {
      if (other != null) Close(other);
      if (!standIn.HasExited) standIn.Kill();
    }
  }

  [Test]
  public async Task Test_SettingAnObjectField_KeepsNoReference()
  {
    int self = Environment.ProcessId;
    dynamic list = RemoteClient.CreateInstance(ListType);
    var field = RemoteClient.GetInstanceType(ListType)
      .GetField("_syncRoot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    int before = ClientPins(self);

    SetToNewObject(field, list);
    field.SetValue((object)list, null);

    // The value's proxy is gone and the setter's readback was returned.
    Assert.That(await Eventually(() => ClientPins(self) <= before, TimeSpan.FromSeconds(30)), Is.True);
    GC.KeepAlive(list);
  }

  /// <summary>A separate frame, so no local keeps the value's proxy reachable.</summary>
  private static void SetToNewObject(FieldInfo field, dynamic list) =>
    field.SetValue((object)list, (object)RemoteClient.CreateInstance("System.Object"));

  [Test]
  public async Task Test_LateUnhook_KeepsAnotherClientsHook()
  {
    using var firstStandIn = StartStandIn();
    using var secondStandIn = StartStandIn();
    var first = ConnectOther(firstStandIn);
    var second = ConnectOther(secondStandIn);
    try
    {
      static void Release(DiverCommunicator client, ObjectOrRemoteAddress instance, ObjectOrRemoteAddress[] args)
      {
        foreach (var argument in args.Prepend(instance))
          if (argument is { IsRemoteAddress: true, IsNull: false, IsType: false })
            client.UnpinObject(argument.RemoteAddress);
      }
      var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      LocalHookCallback firstHook = (_, instance, args) => Release(first, instance, args);
      LocalHookCallback secondHook = (_, instance, args) =>
      {
        Release(second, instance, args);
        fired.TrySetResult(true);
      };
      // Both share one native hook on Component.Dispose().
      Assert.That(first.HookMethod("System.ComponentModel.Component", "Dispose",
        HarmonyPatchPosition.Prefix, firstHook, new()), Is.True);
      Assert.That(second.HookMethod("System.ComponentModel.Component", "Dispose",
        HarmonyPatchPosition.Prefix, secondHook, new()), Is.True);

      // The first client leaves (its share is removed with it), then cleans up late.
      Assert.That(first.UnregisterClient(firstStandIn.Id), Is.True);
      first.UnhookMethod(firstHook);

      ((dynamic)RemoteClient.CreateInstance("System.ComponentModel.Component")).Dispose();
      Assert.That(await fired.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.True,
        "the second client's hook still fires");
      second.UnhookMethod(secondHook);
    }
    finally
    {
      Close(first);
      Close(second);
      firstStandIn.Kill();
      secondStandIn.Kill();
    }
  }

  [Test]
  public async Task Test_LateEventUnsubscribe_KeepsAnotherClientsSubscription()
  {
    using var firstStandIn = StartStandIn();
    using var secondStandIn = StartStandIn();
    var first = ConnectOther(firstStandIn);
    var second = ConnectOther(secondStandIn);
    try
    {
      dynamic component = RemoteClient.CreateInstance("System.ComponentModel.Component");
      var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      DiverCommunicator.LocalEventCallback firstHandler = (_, args) => ReleaseArguments(first, args);
      DiverCommunicator.LocalEventCallback secondHandler = (_, args) =>
      {
        ReleaseArguments(second, args);
        fired.TrySetResult(true);
      };
      first.EventSubscribe(Token(component), "Disposed", firstHandler);
      second.EventSubscribe(Token(component), "Disposed", secondHandler);

      // The first client leaves (its subscription is removed with it), then
      // cleans up late, as a client that did not see the confirmation would:
      // the Diver accepts it and removes nothing of the second client's.
      Assert.That(first.UnregisterClient(firstStandIn.Id), Is.True);
      ((System.Collections.IDictionary)PrivateField(typeof(DiverCommunicator), "_eventEpochs")
        .GetValue(first)!).Clear();
      Assert.That(() => first.EventUnsubscribe(firstHandler), Throws.Nothing);

      component.Dispose();
      Assert.That(await fired.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.True,
        "the second client's subscription still fires");
      second.EventUnsubscribe(secondHandler);
    }
    finally
    {
      Close(first);
      Close(second);
      firstStandIn.Kill();
      secondStandIn.Kill();
    }
  }

  /// <summary>Returns a callback's object references, as a handler that keeps none does.</summary>
  private static void ReleaseArguments(DiverCommunicator client, IEnumerable<ObjectOrRemoteAddress> arguments)
  {
    foreach (var argument in arguments)
      if (argument is { IsRemoteAddress: true, IsNull: false, IsType: false })
        client.UnpinObject(argument.RemoteAddress);
  }

  [Test]
  public async Task Test_ReconnectedClient_KeepsItsRegistration()
  {
    using var standIn = StartStandIn();
    var other = ConnectOther(standIn);
    try
    {
      ulong token = other.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;
      int connections = RemoteClient.GetDiverDiagnostics()!.ConnectedClients;

      // Its connection drops; explicit and automatic reconnects then overlap.
      DropConnection(other);
      await Task.WhenAll(Enumerable.Range(0, 6).Select(i => i % 2 == 0
        ? other.ConnectAsync()
        : Task.Run(() => Assert.That(other.CheckAliveness(), Is.True))));

      Assert.That(await Eventually(() => RemoteClient.GetDiverDiagnostics()!.ConnectedClients == connections,
        TimeSpan.FromSeconds(10)), Is.True, "one new connection replaces the dropped one");
      // The new connection was registered before its first request, so this
      // release is the client's own.
      other.UnpinObject(token);
      Assert.That(IsPinned(token), Is.False);
    }
    finally
    {
      Close(other);
      standIn.Kill();
    }
  }

  [Test]
  public async Task Test_OlderClient_ReconnectedByPing_ReceivesCallbacks()
  {
    using var standIn = StartStandIn();
    var older = ConnectOlder(standIn);
    try
    {
      dynamic component = RemoteClient.CreateInstance("System.ComponentModel.Component");
      var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      DiverCommunicator.LocalEventCallback handler = (_, args) =>
      {
        ReleaseArguments(older, args);
        delivered.TrySetResult(true);
      };
      older.EventSubscribe(Token(component), "Disposed", handler);

      // It reconnects through its liveness check alone, without registering.
      DropConnection(older);
      Assert.That(older.CheckAliveness(), Is.True);

      component.Dispose();
      Assert.That(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.True,
        "an older client's reconnected connection receives callbacks, as before");
      older.EventUnsubscribe(handler);
    }
    finally
    {
      Close(older);
      standIn.Kill();
    }
  }

  [Test]
  public async Task Test_OlderClientsProbe_RetainsNoCallbackArgument()
  {
    using var standIn = StartStandIn();
    var older = ConnectOlder(standIn);
    var probe = ConnectOlderProbe();
    try
    {
      // A hook's arguments arrive as a new object array on every call.
      var delivered = new TaskCompletionSource<ObjectOrRemoteAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
      LocalHookCallback hook = (_, instance, args) =>
      {
        // As an older client does when its proxies of the arguments are released.
        ReleaseArguments(older, args.Prepend(instance));
        delivered.TrySetResult(args[0]);
      };
      Assert.That(older.HookMethod("System.ComponentModel.Component", "Dispose",
        HarmonyPatchPosition.Prefix, hook, new()), Is.True);

      ((dynamic)RemoteClient.CreateInstance("System.ComponentModel.Component")).Dispose();
      var arguments = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

      // The older client and its probe share one reference, as with the old
      // Diver; the client's release returns it although the probe ignored it.
      Assert.That(await Eventually(() => !IsPinned(arguments.RemoteAddress, arguments.Type),
        TimeSpan.FromSeconds(10)), Is.True, "the callback's argument is not retained");
      older.UnhookMethod(hook);
    }
    finally
    {
      Close(probe);
      Close(older);
      standIn.Kill();
    }
  }

  [Test]
  public async Task Test_CurrentClientsCallback_BesideOlderClients_RetainsNoArgument()
  {
    using var olderStandIn = StartStandIn();
    using var currentStandIn = StartStandIn();
    var older = ConnectOlder(olderStandIn);
    var probe = ConnectOlderProbe();
    var current = ConnectOther(currentStandIn);
    try
    {
      // Only the current client handles this hook; the older client and its
      // probe receive its callbacks and ignore them.
      Assert.That(older.CheckAliveness(), Is.True);
      var delivered = new TaskCompletionSource<ObjectOrRemoteAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
      LocalHookCallback hook = (_, instance, args) =>
      {
        ReleaseArguments(current, args.Prepend(instance));
        delivered.TrySetResult(args[0]);
      };
      Assert.That(current.HookMethod("System.ComponentModel.Component", "Dispose",
        HarmonyPatchPosition.Prefix, hook, new()), Is.True);

      ((dynamic)RemoteClient.CreateInstance("System.ComponentModel.Component")).Dispose();
      var arguments = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

      Assert.That(await Eventually(() => !IsPinned(arguments.RemoteAddress, arguments.Type),
        TimeSpan.FromSeconds(10)), Is.True, "no shared reference is left for the older connections");
      current.UnhookMethod(hook);
    }
    finally
    {
      Close(current);
      Close(probe);
      Close(older);
      olderStandIn.Kill();
      currentStandIn.Kill();
    }
  }

  [Test]
  public void Test_OlderClientsInOneProcess_KeepEachOthersPins()
  {
    using var standIn = StartStandIn();
    var first = ConnectOlder(standIn);
    var second = ConnectOlder(standIn);
    try
    {
      ulong kept = second.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;

      Assert.That(first.UnregisterClient(standIn.Id), Is.True);
      Assert.That(IsPinned(kept), Is.True, "the other older client of the same process keeps its pin");
      second.UnpinObject(kept);
      Assert.That(IsPinned(kept), Is.False);
    }
    finally
    {
      Close(first);
      Close(second);
      standIn.Kill();
    }
  }

  [Test]
  public async Task Test_OlderClient_PinsAreKeptAfterItExits()
  {
    using var standIn = StartStandIn();
    DiverCommunicator? older = ConnectOlder(standIn);
    try
    {
      ulong token = older.CreateObject(ListType, []).ReturnedObjectOrAddress!.RemoteAddress;

      // An older client's pins are shared with its peers (it cannot be told
      // apart from them once it reconnects), so they are not reclaimed.
      Close(older);
      older = null;
      standIn.Kill();
      standIn.WaitForExit();
      await Task.Delay(TimeSpan.FromSeconds(12));
      Assert.That(IsPinned(token), Is.True, "kept, as before");

      Communicator.UnpinObject(token);
      Assert.That(IsPinned(token), Is.False);
    }
    finally
    {
      if (older != null) Close(older);
      if (!standIn.HasExited) standIn.Kill();
    }
  }

  [Test]
  public async Task Test_OlderClientsUnhook_RemovesOnlyItsShare()
  {
    using var olderStandIn = StartStandIn();
    using var currentStandIn = StartStandIn();
    var older = ConnectOlder(olderStandIn);
    var current = ConnectOther(currentStandIn);
    try
    {
      var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      LocalHookCallback olderHook = (_, instance, args) => ReleaseArguments(older, args.Prepend(instance));
      LocalHookCallback currentHook = (_, instance, args) =>
      {
        ReleaseArguments(current, args.Prepend(instance));
        fired.TrySetResult(true);
      };
      // Both share one native hook on Component.Dispose().
      Assert.That(older.HookMethod("System.ComponentModel.Component", "Dispose",
        HarmonyPatchPosition.Prefix, olderHook, new()), Is.True);
      Assert.That(current.HookMethod("System.ComponentModel.Component", "Dispose",
        HarmonyPatchPosition.Prefix, currentHook, new()), Is.True);

      // The older client unhooks over a reconnected connection, then leaves
      // and its process exits: its share is removed exactly once.
      DropConnection(older);
      older.UnhookMethod(olderHook);
      Assert.That(older.UnregisterClient(olderStandIn.Id), Is.True);
      olderStandIn.Kill();
      await Task.Delay(TimeSpan.FromSeconds(12));

      ((dynamic)RemoteClient.CreateInstance("System.ComponentModel.Component")).Dispose();
      Assert.That(await fired.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.True,
        "the current client's hook still fires");
      current.UnhookMethod(currentHook);
    }
    finally
    {
      Close(older);
      Close(current);
      if (!olderStandIn.HasExited) olderStandIn.Kill();
      currentStandIn.Kill();
    }
  }

  [Test]
  public void Test_FailedBatchRead_ReturnsItsItemPins()
  {
    // The second item is null, so pinning it fails after the first was pinned.
    dynamic list = RemoteClient.CreateInstance(ListType);
    list.Add(RemoteClient.CreateInstance(ListType));
    list.Add(null);

    using var standIn = StartStandIn();
    var other = ConnectOther(standIn);
    try
    {
      Assert.Throws<ExternalErrorException>(() =>
        other.GetBatchCollectionMembers(Token(list), ListType, "_size", 0, pinItems: true));
      Assert.That(ClientPins(standIn.Id), Is.EqualTo(0), "the failed read returned what it pinned");
    }
    finally
    {
      Close(other);
      standIn.Kill();
    }
  }

  [Test]
  public void Test_CollectionSnapshot_KeepsNoItemPins()
  {
    int self = Environment.ProcessId;
    dynamic list = RemoteClient.CreateInstance(ListType);
    var items = Enumerable.Range(0, 3).Select(_ => RemoteClient.CreateInstance(ListType)).ToList();
    foreach (var item in items) list.Add(item);
    var tryFetch = typeof(DiverCommunicator).Assembly
      .GetType("MTGOSDK.Core.Reflection.Serialization.RemoteBatchCollection")!
      .GetMethod("TryFetch")!;
    int before = ClientPins(self);

    // As collection and standings reads do: only the columns are used.
    for (int i = 0; i < 5; i++)
      Assert.That((bool)tryFetch.Invoke(null, [(object)list, new[] { "_size" }, null, 0])!, Is.True);

    Assert.That(ClientPins(self), Is.LessThanOrEqualTo(before));
    GC.KeepAlive(items);
  }
}
