/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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
  public void Test_UnattributedReferences_BehaveAsBefore()
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
  public void Test_AnUnregisteredClientsPin_IsUnattributed()
  {
    const int unknown = 1003;
    ulong token = s_runtime.PinObject(new object(), unknown);

    Assert.That(s_runtime.GetClientReferenceCount(unknown), Is.EqualTo(0));
    Assert.That(s_runtime.UnpinObject(token, unknown), Is.False, "not a registered client");
    Assert.That(s_runtime.UnpinObject(token, null), Is.True);
    Assert.That(IsPinned(token), Is.False);
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
