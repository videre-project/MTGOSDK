/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Memory.Snapshot;

using ScubaDiver;


namespace MTGOSDK.UnitTests;

/// <summary>
/// Registered requests acquire and return references for their own registration.
/// Removed registrations and closed, unregistered connections cannot acquire pins.
/// </summary>
public class RequestPins
{
  private const int ClientA = 3001;

  private SnapshotRuntime _runtime = null!;

  [OneTimeSetUp]
  public void CreateRuntime() => _runtime = new SnapshotRuntime();

  [OneTimeTearDown]
  public void DisposeRuntime() => _runtime.Dispose();

  [SetUp]
  public void Reset()
  {
    _runtime.RemoveClient(ClientA);
    _runtime.UnpinAllObjects();
    _runtime.AddClient(ClientA);
  }

  private bool IsPinned(ulong token) => _runtime.TryGetPinnedObject(token, out _);

  [Test]
  public void Test_ClosedUnregisteredConnection_CannotAcquireOrReleasePins()
  {
    // A request queued on a connection that closed after its registration was removed.
    var owner = RequestOwner.Resolve(null, open: false);
    var instance = new object();
    ulong shared = _runtime.PinObject(instance, null);

    Assert.That(() => owner.Pin(_runtime, instance), Throws.InstanceOf<PinRefusedException>());
    Assert.That(owner.Unpin(_runtime, shared), Is.False, "it takes no other client's shared reference");
    Assert.That(_runtime.UnpinObject(shared, null), Is.True, "the shared reference is intact");
    Assert.That(IsPinned(shared), Is.False, "and was the only one");
  }

  [TestCase(false)]
  [TestCase(true)]
  public void Test_RemovedRegistration_CannotAcquirePins(bool connectionOpen)
  {
    // Removing a mapped registration must prevent further pins on either connection state.
    var owner = RequestOwner.Resolve(ClientA, open: connectionOpen);
    _runtime.RemoveClient(ClientA);
    var instance = new object();

    Assert.That(() => owner.Pin(_runtime, instance), Throws.InstanceOf<PinRefusedException>());
    Assert.That(IsPinned(_runtime.GetObjectToken(instance)), Is.False);
  }

  [Test]
  public void Test_RegisteredAndUnregisteredRequests_HoldIndependentReferences()
  {
    var instance = new object();
    var registered = RequestOwner.Resolve(ClientA, open: true);
    var unregistered = RequestOwner.Resolve(null, open: true);
    ulong token = registered.Pin(_runtime, instance);
    Assert.That(unregistered.Pin(_runtime, instance), Is.EqualTo(token));
    Assert.That(_runtime.GetClientReferenceCount(ClientA), Is.EqualTo(1));

    Assert.That(registered.Unpin(_runtime, token), Is.True);
    Assert.That(IsPinned(token), Is.True, "the unregistered request still holds its shared reference");
    Assert.That(unregistered.Unpin(_runtime, token), Is.True);
    Assert.That(IsPinned(token), Is.False);
  }

  [Test]
  public void Test_ReleaseFromPreviousRegistration_PreservesCurrentPin()
  {
    var instance = new object();
    ulong token = RequestOwner.Of(ClientA).Pin(_runtime, instance);
    var owner = RequestOwner.Of(ClientA);

    Assert.That(owner.Unpin(_runtime, token, receiptOwner: "previous", connectionOwner: "current"), Is.False);
    Assert.That(IsPinned(token), Is.True, "the current registration still owns its reference");
    Assert.That(owner.Unpin(_runtime, token, receiptOwner: "current", connectionOwner: "current"), Is.True);
    Assert.That(IsPinned(token), Is.False);
  }

  [Test]
  public void Test_ReleaseWithoutReceiptOwner_UsesConnectionRegistration()
  {
    ulong token = RequestOwner.Of(ClientA).Pin(_runtime, new object());
    Assert.That(RequestOwner.Of(ClientA).Unpin(_runtime, token, receiptOwner: null, connectionOwner: "current"),
      Is.True);
    Assert.That(IsPinned(token), Is.False);
  }

}
