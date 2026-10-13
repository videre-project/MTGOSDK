/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Memory.Snapshot;


namespace ScubaDiver;

/// <summary>
/// Whom a request's pin references are counted for (kept free of the Diver's
/// state, so it can be tested on its own).
/// </summary>
internal readonly struct RequestOwner
{
  private RequestOwner(bool gone, int? registration)
  {
    IsGone = gone;
    Registration = registration;
  }

  /// <summary>
  /// The connection closed and is not mapped to a registration: it never
  /// registered, or its registration was removed. A reference taken for it
  /// could never be returned (its response cannot be delivered), so every
  /// pin is refused, and so is every release: a release from a removed
  /// registration's connection must not take another client's unattributed
  /// reference. An older client's release still queued when its connection
  /// closed is refused too; that reference then stays until the last client
  /// leaves, as an older client's leftover references always do.
  /// </summary>
  public bool IsGone { get; }

  /// <summary>
  /// The registration the connection was registered under (pins for one
  /// that was removed are refused), or null for an open connection that
  /// never registered (or a request without a connection): unattributed.
  /// </summary>
  public int? Registration { get; }

  public static RequestOwner Gone => new(true, null);
  public static RequestOwner Unregistered => new(false, null);
  public static RequestOwner Of(int registration) => new(false, registration);

  /// <summary>
  /// The owner of a connection: its registration while it is mapped to one
  /// (an open connection stays mapped after its registration was removed),
  /// otherwise unregistered while open and gone once closed.
  /// </summary>
  public static RequestOwner Resolve(int? mappedRegistration, bool open) =>
    mappedRegistration is int registration ? Of(registration) :
    open ? Unregistered :
    Gone;

  /// <summary>
  /// Takes one pin reference for this owner. Refused (<see cref="PinRefusedException"/>)
  /// for a removed registration or a gone connection, so the request fails
  /// rather than return a reference its client can never release; whatever
  /// the request did before (an invocation, a field write) is not undone.
  /// </summary>
  public ulong Pin(SnapshotRuntime runtime, object instance)
  {
    if (IsGone)
      throw new PinRefusedException("The request's connection has closed; no pin was taken.");
    return runtime.PinObject(instance, Registration);
  }

  /// <summary>
  /// Returns one of this owner's pin references (nothing for a gone connection).
  /// </summary>
  public bool Unpin(SnapshotRuntime runtime, ulong token) =>
    !IsGone && runtime.UnpinObject(token, Registration);
}
