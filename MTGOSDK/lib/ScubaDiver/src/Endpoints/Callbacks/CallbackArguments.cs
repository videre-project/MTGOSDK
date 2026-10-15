/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Remoting.Interop;


namespace ScubaDiver;

/// <summary>
/// Encodes a callback's arguments and takes the pin references its recipients
/// own (kept free of the Diver's state, so it can be tested on its own).
/// </summary>
internal static class CallbackArguments
{
  /// <summary>
  /// Takes one pin reference, or refuses one for a removed registration.
  /// </summary>
  public delegate bool TryPin(object instance, int? client, out ulong token);

  /// <summary>
  /// The encoded arguments and the references taken for them. A recipient in
  /// neither <see cref="Owners"/> nor <see cref="SharedRecipients"/> is not
  /// sent the callback: it no longer receives, or its registration was removed
  /// while the references were taken (what it got before was reclaimed with
  /// the removal).
  /// </summary>
  /// <param name="Parameters">
  /// The encoded arguments, in the order they were passed to the callback.
  /// </param>
  /// <param name="Tokens">
  /// The token of each object argument, once per occurrence.
  /// </param>
  /// <param name="Owners">
  /// The recipients that own one reference to each object argument (their
  /// registration ids). A recipient dropped while the references were taken
  /// is not among them.
  /// </param>
  /// <param name="SharedRecipients">
  /// The recipients without a registration that receive the callback with
  /// the shared reference (or, without one, no reference).
  /// </param>
  /// <param name="Shared">
  /// Whether one shared, unattributed reference was taken to each object argument.
  /// </param>
  public sealed record Prepared(
    ObjectOrRemoteAddress[] Parameters,
    List<ulong> Tokens,
    Dictionary<Guid, int> Owners,
    HashSet<Guid> SharedRecipients,
    bool Shared);

  /// <summary>
  /// Encodes <paramref name="parameters"/> for <paramref name="recipients"/>.
  /// Every registered recipient takes one reference to each object argument,
  /// counted for its registration; a recipient whose registration is removed
  /// meanwhile (a refused pin) is dropped and never falls back to the shared
  /// reference. Recipients without a registration share one unattributed
  /// reference per argument when <paramref name="hasUnregisteredSubscriber"/>
  /// says one of them can return it. If encoding an argument throws, every
  /// reference taken so far is returned before the exception propagates.
  /// </summary>
  /// <param name="recipientOf">
  /// Whether a recipient still receives callbacks, and its registration (null:
  /// a connection without one).
  /// </param>
  public static Prepared Prepare(
    object?[] parameters,
    IReadOnlyList<Guid> recipients,
    Func<Guid, (bool Receives, int? Client)> recipientOf,
    Func<bool> hasUnregisteredSubscriber,
    TryPin tryPin,
    Func<ulong, int?, bool> unpin,
    Func<object, ulong> tokenOf)
  {
    var owners = new Dictionary<Guid, int>();
    var sharedRecipients = new HashSet<Guid>();
    foreach (Guid recipient in recipients)
    {
      var (receives, client) = recipientOf(recipient);
      if (!receives) continue;
      if (client is int id) owners[recipient] = id;
      else sharedRecipients.Add(recipient);
    }
    // Without such a subscriber, the shared reference would never be returned.
    bool shared = sharedRecipients.Count > 0 && hasUnregisteredSubscriber();

    var encoded = new ObjectOrRemoteAddress[parameters.Length];
    var tokens = new List<ulong>(parameters.Length);
    // Every reference taken, so a failure returns exactly those.
    var taken = new List<(ulong Token, int? Client)>();
    try
    {
      for (int i = 0; i < parameters.Length; i++)
      {
        object? parameter = parameters[i];
        if (parameter == null)
        {
          encoded[i] = ObjectOrRemoteAddress.Null;
          continue;
        }
        if (parameter.GetType().IsPrimitiveEtc())
        {
          encoded[i] = ObjectOrRemoteAddress.FromObj(parameter);
          continue;
        }

        string typeName = parameter.GetType().FullName!;
        int hashCode = parameter.GetHashCode();
        ulong token = tokenOf(parameter);
        foreach (var owner in owners.ToList())
        {
          if (tryPin(parameter, owner.Value, out token))
          {
            taken.Add((token, owner.Value));
          }
          else
          {
            owners.Remove(owner.Key);
            token = tokenOf(parameter);
          }
        }
        if (shared)
        {
          if (!tryPin(parameter, null, out token))
            throw new InvalidOperationException("An unattributed pin was refused.");
          taken.Add((token, null));
        }
        tokens.Add(token);
        encoded[i] = ObjectOrRemoteAddress.FromToken(token, typeName, hashCode);
      }
    }
    catch
    {
      // A recipient removed meanwhile had its references reclaimed: those
      // releases are refused, as they should be.
      foreach (var (token, client) in taken)
        unpin(token, client);
      throw;
    }

    return new Prepared(encoded, tokens, owners, sharedRecipients, shared);
  }
}
