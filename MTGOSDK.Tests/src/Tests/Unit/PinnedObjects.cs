/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Memory.Snapshot;


// Outside the MTGOSDK.Tests namespace: these tests need no MTGO client, so the
// SetupFixture that launches and logs in to one does not run for them.
namespace MTGOSDK.UnitTests;

/// <summary>
/// The Diver's pin table counts every reference per registered client, so a
/// client that exits without returning its references can be reclaimed alone.
/// </summary>
public class PinnedObjects
{
  private const int ClientA = 1001;
  private const int ClientB = 1002;

  private static SnapshotRuntime s_runtime = null!;

  [OneTimeSetUp]
  public void CreateRuntime() => s_runtime = new SnapshotRuntime();

  [OneTimeTearDown]
  public void DisposeRuntime() => s_runtime.Dispose();

  [SetUp]
  public void Reset()
  {
    s_runtime.RemoveClient(ClientA);
    s_runtime.RemoveClient(ClientB);
    s_runtime.UnpinAllObjects();
    s_runtime.AddClient(ClientA);
    s_runtime.AddClient(ClientB);
  }

  private static bool IsPinned(ulong token) =>
    s_runtime.TryGetPinnedObject(token, out _);

  [Test]
  public void Test_RemovingAClient_KeepsOtherClientsReferences()
  {
    var shared = new object();
    var onlyA = new object();
    ulong sharedToken = s_runtime.PinObject(shared, ClientA);
    Assert.That(s_runtime.PinObject(shared, ClientA), Is.EqualTo(sharedToken));
    Assert.That(s_runtime.PinObject(shared, ClientB), Is.EqualTo(sharedToken));
    ulong onlyAToken = s_runtime.PinObject(onlyA, ClientA);

    Assert.That(s_runtime.RemoveClient(ClientA), Is.EqualTo(3));
    Assert.That(IsPinned(onlyAToken), Is.False, "only A held it");
    Assert.That(IsPinned(sharedToken), Is.True, "B still holds it");
    Assert.That(s_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(1));

    Assert.That(s_runtime.UnpinObject(sharedToken, ClientB), Is.True);
    Assert.That(IsPinned(sharedToken), Is.False);
  }

  [Test]
  public void Test_AClientCannotReleaseAnotherClientsReference()
  {
    ulong token = s_runtime.PinObject(new object(), ClientA);

    Assert.That(s_runtime.UnpinObject(token, ClientB), Is.False);
    Assert.That(IsPinned(token), Is.True);
    Assert.That(s_runtime.UnpinObject(token, ClientA), Is.True);
    Assert.That(IsPinned(token), Is.False);
  }

  [Test]
  public void Test_ARemovedClientsLateReleaseIsRefused()
  {
    var instance = new object();
    s_runtime.PinObject(instance, ClientA);
    s_runtime.RemoveClient(ClientA);
    ulong token = s_runtime.PinObject(instance, ClientB);
    s_runtime.PinObject(instance, null);

    // A finalizer of the removed client releasing after the reclaim.
    Assert.That(s_runtime.UnpinObject(token, ClientA), Is.False);
    Assert.That(s_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(1));
    Assert.That(s_runtime.UnpinObject(token, null), Is.True, "the unattributed reference");
    Assert.That(s_runtime.UnpinObject(token, ClientB), Is.True);
    Assert.That(IsPinned(token), Is.False);
  }

  [Test]
  public void Test_UnattributedPins_ReleaseOneReferenceAtATime()
  {
    var instance = new object();
    ulong token = s_runtime.PinObject(instance);
    Assert.That(s_runtime.PinObject(instance), Is.EqualTo(token));

    Assert.That(s_runtime.UnpinObject(token), Is.True);
    Assert.That(IsPinned(token), Is.True, "one reference left");
    Assert.That(s_runtime.UnpinObject(token), Is.True);
    Assert.That(IsPinned(token), Is.False);
    Assert.That(s_runtime.UnpinObject(token), Is.False, "nothing left to release");
  }

  [Test]
  public void Test_ARegisteredClient_ReleasesAnUnattributedReference()
  {
    // A callback delivered over a connection before it registered.
    ulong token = s_runtime.PinObject(new object(), null);

    Assert.That(s_runtime.UnpinObject(token, ClientA), Is.True);
    Assert.That(IsPinned(token), Is.False);
  }

  [Test]
  public void Test_APinForAnUnknownOrRemovedClient_IsRefused()
  {
    const int unknown = 1003;
    var instance = new object();
    ulong token = s_runtime.GetObjectToken(instance);

    Assert.That(s_runtime.TryPinObject(instance, unknown, out _), Is.False);
    Assert.That(() => s_runtime.PinObject(instance, unknown), Throws.InstanceOf<PinRefusedException>());
    s_runtime.RemoveClient(ClientA);
    Assert.That(s_runtime.TryPinObject(instance, ClientA, out _), Is.False, "removed");
    Assert.That(IsPinned(token), Is.False, "nothing was pinned");
    Assert.That(s_runtime.GetClientReferenceCount(unknown), Is.EqualTo(0));
    Assert.That(s_runtime.UnpinObject(token, null), Is.False, "nothing unattributed either");
  }

  [Test]
  public void Test_APinTakenBeforeItsClientIsRemoved_IsReclaimed()
  {
    var instance = new object();
    ulong token = s_runtime.PinObject(instance, ClientA);
    Assert.That(s_runtime.PinObject(instance, ClientB), Is.EqualTo(token));

    Assert.That(s_runtime.RemoveClient(ClientA), Is.EqualTo(1));
    Assert.That(IsPinned(token), Is.True, "B still holds it");
    Assert.That(s_runtime.UnpinObject(token, ClientB), Is.True);
    Assert.That(IsPinned(token), Is.False, "no reference is left behind");
  }

  [Test]
  public void Test_ARemovalBetweenSeveralPins_LeavesNoReference()
  {
    // A request (or callback) pinning several objects for A while A is removed.
    var first = new object();
    var second = new object();
    ulong firstToken = s_runtime.PinObject(first, ClientA);
    s_runtime.RemoveClient(ClientA);
    Assert.That(s_runtime.TryPinObject(second, ClientA, out _), Is.False);

    Assert.That(IsPinned(firstToken), Is.False, "reclaimed with the removal");
    Assert.That(IsPinned(s_runtime.GetObjectToken(second)), Is.False, "refused");
    Assert.That(s_runtime.UnpinObject(firstToken, ClientA), Is.False, "the cleanup of the failed request is harmless");
    Assert.That(s_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(0), "B is untouched");
  }

  [Test]
  public void Test_UnpinAll_KeepsClientsRegistered()
  {
    ulong token = s_runtime.PinObject(new object(), ClientA);
    s_runtime.UnpinAllObjects();

    Assert.That(IsPinned(token), Is.False);
    Assert.That(s_runtime.GetClientReferenceCount(ClientA), Is.EqualTo(0));
    ulong next = s_runtime.PinObject(new object(), ClientA);
    Assert.That(s_runtime.GetClientReferenceCount(ClientA), Is.EqualTo(1), "still counted for A");
    Assert.That(s_runtime.RemoveClient(ClientA), Is.EqualTo(1));
    Assert.That(IsPinned(next), Is.False);
  }
}
