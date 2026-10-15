/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.Core.Memory.Snapshot;

using ScubaDiver;


namespace MTGOSDK.UnitTests;

/// <summary>
/// Registered recipients own one reference per occurrence of an object argument.
/// Legacy subscribers use shared references; removed recipients are excluded.
/// Failed encoding returns the references acquired for that callback.
/// </summary>
public class CallbackPins
{
  private const int ClientA = 2001;
  private const int ClientB = 2002;

  private static readonly Guid s_recipientA = Guid.NewGuid();
  private static readonly Guid s_recipientB = Guid.NewGuid();
  private static readonly Guid s_unregisteredRecipient = Guid.NewGuid();
  private static readonly Guid s_inactiveRecipient = Guid.NewGuid();

  private SnapshotRuntime _runtime = null!;

  [OneTimeSetUp]
  public void CreateRuntime() => _runtime = new SnapshotRuntime();

  [OneTimeTearDown]
  public void DisposeRuntime() => _runtime.Dispose();

  [SetUp]
  public void Reset()
  {
    _runtime.RemoveClient(ClientA);
    _runtime.RemoveClient(ClientB);
    _runtime.UnpinAllObjects();
    _runtime.AddClient(ClientA);
    _runtime.AddClient(ClientB);
  }

  private bool IsPinned(ulong token) => _runtime.TryGetPinnedObject(token, out _);

  private static (bool, int?) RecipientOf(Guid recipient) =>
    recipient == s_recipientA ? (true, ClientA) :
    recipient == s_recipientB ? (true, ClientB) :
    recipient == s_unregisteredRecipient ? (true, null) :
    (false, null);

  [Test]
  public void Test_ARecipientRemovedWhileItsReferencesAreTaken_IsDropped()
  {
    var first = new object();
    var second = new object();
    // An object twice: one reference per occurrence.
    object?[] parameters = [first, 7, second, first];
    int pinsForA = 0;
    var sharedPins = new List<ulong>();

    var prepared = CallbackArguments.Prepare(
      parameters,
      [s_recipientA, s_recipientB, s_unregisteredRecipient],
      RecipientOf,
      () => true,
      (object instance, int? client, out ulong token) =>
      {
        // A unregisters just before its second reference is taken.
        if (client == ClientA && ++pinsForA == 2)
          _runtime.RemoveClient(ClientA);
        bool taken = _runtime.TryPinObject(instance, client, out token);
        if (taken && client == null) sharedPins.Add(token);
        return taken;
      },
      _runtime.UnpinObject,
      _runtime.GetObjectToken);

    Assert.That(prepared.Owners.Keys, Is.EquivalentTo(new[] { s_recipientB }), "A is not sent the callback");
    Assert.That(prepared.SharedRecipients, Is.EquivalentTo(new[] { s_unregisteredRecipient }),
      "the removed recipient is excluded from shared delivery too");
    Assert.That(prepared.Shared, Is.True);
    Assert.That(sharedPins, Has.Count.EqualTo(3), "one shared reference per object argument");
    Assert.That(prepared.Tokens, Has.Count.EqualTo(3));
    Assert.That(prepared.Parameters[1].IsRemoteAddress, Is.False, "a primitive is sent by value");

    Assert.That(_runtime.GetClientReferenceCount(ClientA), Is.EqualTo(0),
      "removing the registration returns all of its references");
    Assert.That(_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(3));

    // B and the older recipient return theirs: nothing is left pinned.
    foreach (ulong token in prepared.Tokens)
    {
      Assert.That(_runtime.UnpinObject(token, ClientB), Is.True);
      Assert.That(_runtime.UnpinObject(token, null), Is.True);
    }
    Assert.That(prepared.Tokens.Distinct().Any(IsPinned), Is.False);
  }

  /// <summary>
  /// An argument whose hash code cannot be read.
  /// </summary>
  private sealed class ThrowingHashCode
  {
    public override int GetHashCode() => throw new InvalidOperationException("hash code unavailable");
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
  }

  [Test]
  public void Test_AFailingArgument_ReturnsTheReferencesAlreadyTaken()
  {
    var first = new object();
    object?[] parameters = [first, first, new ThrowingHashCode()];

    Assert.That(() => CallbackArguments.Prepare(
      parameters,
      [s_recipientA, s_recipientB, s_unregisteredRecipient],
      RecipientOf,
      () => true,
      _runtime.TryPinObject,
      _runtime.UnpinObject,
      _runtime.GetObjectToken), Throws.InvalidOperationException.With.Message.EqualTo("hash code unavailable"));

    Assert.That(_runtime.GetClientReferenceCount(ClientA), Is.EqualTo(0));
    Assert.That(_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(0));
    Assert.That(IsPinned(_runtime.GetObjectToken(first)), Is.False, "no shared reference is left either");
  }

  [Test]
  public void Test_AFailingArgument_KeepsAnotherHoldersReference()
  {
    var first = new object();
    ulong held = _runtime.PinObject(first, ClientB);

    Assert.That(() => CallbackArguments.Prepare(
      [first, new ThrowingHashCode()],
      [s_recipientB],
      RecipientOf,
      () => false,
      _runtime.TryPinObject,
      _runtime.UnpinObject,
      _runtime.GetObjectToken), Throws.InvalidOperationException.With.Message.EqualTo("hash code unavailable"));

    Assert.That(_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(1), "only the callback's reference is returned");
    Assert.That(IsPinned(held), Is.True);
  }

  [Test]
  public void Test_ARecipientNoLongerReceiving_GetsNothing()
  {
    var argument = new object();
    var prepared = CallbackArguments.Prepare(
      [argument],
      [s_inactiveRecipient, s_recipientB],
      RecipientOf,
      () => true,
      _runtime.TryPinObject,
      _runtime.UnpinObject,
      _runtime.GetObjectToken);

    Assert.That(prepared.Owners.Keys, Is.EquivalentTo(new[] { s_recipientB }));
    Assert.That(prepared.SharedRecipients, Is.Empty);
    Assert.That(prepared.Shared, Is.False, "no recipient without a registration");
    Assert.That(_runtime.GetClientReferenceCount(ClientB), Is.EqualTo(1));
    Assert.That(_runtime.UnpinObject(prepared.Tokens[0], ClientB), Is.True);
    Assert.That(IsPinned(prepared.Tokens[0]), Is.False);
  }

  [Test]
  public void Test_NoSharedReference_WithoutASubscriberToReturnIt()
  {
    var argument = new object();
    var prepared = CallbackArguments.Prepare(
      [argument],
      [s_unregisteredRecipient],
      RecipientOf,
      () => false,
      _runtime.TryPinObject,
      _runtime.UnpinObject,
      _runtime.GetObjectToken);

    Assert.That(prepared.SharedRecipients, Is.EquivalentTo(new[] { s_unregisteredRecipient }),
      "still sent, owning nothing");
    Assert.That(prepared.Shared, Is.False);
    Assert.That(IsPinned(prepared.Tokens[0]), Is.False);
  }
}
