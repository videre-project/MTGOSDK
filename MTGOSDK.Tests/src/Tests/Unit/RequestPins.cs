/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Memory.Snapshot;

using ScubaDiver;


namespace MTGOSDK.UnitTests;

/// <summary>
/// Whom a request's pins are counted for (RequestOwner, compiled into this
/// project from ScubaDiver), against the real pin table.
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
  public void Test_AConnectionsOwner_IsResolvedFromItsMappingAndState()
  {
    Assert.That(RequestOwner.Resolve(ClientA, open: true).Registration, Is.EqualTo(ClientA));
    Assert.That(RequestOwner.Resolve(ClientA, open: false).Registration, Is.EqualTo(ClientA),
      "a closed connection stays its registration's while that lasts");
    Assert.That(RequestOwner.Resolve(null, open: true), Is.EqualTo(RequestOwner.Unregistered));
    Assert.That(RequestOwner.Resolve(null, open: false).IsGone, Is.True);
  }

  [Test]
  public void Test_AGoneRequest_PinsAndReleasesNothing()
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

  [Test]
  public void Test_ARemovedRegistrationsRequest_IsRefused()
  {
    // Its connection is still open, so still mapped to the removed registration.
    var owner = RequestOwner.Resolve(ClientA, open: true);
    _runtime.RemoveClient(ClientA);
    var instance = new object();

    Assert.That(() => owner.Pin(_runtime, instance), Throws.InstanceOf<PinRefusedException>());
    Assert.That(IsPinned(_runtime.GetObjectToken(instance)), Is.False);
  }

  [Test]
  public void Test_RegisteredAndUnregisteredRequests_Pin()
  {
    var instance = new object();
    ulong token = RequestOwner.Of(ClientA).Pin(_runtime, instance);
    Assert.That(RequestOwner.Unregistered.Pin(_runtime, instance), Is.EqualTo(token));
    Assert.That(_runtime.GetClientReferenceCount(ClientA), Is.EqualTo(1));

    Assert.That(RequestOwner.Of(ClientA).Unpin(_runtime, token), Is.True);
    Assert.That(RequestOwner.Unregistered.Unpin(_runtime, token), Is.True);
    Assert.That(IsPinned(token), Is.False);
  }
}
